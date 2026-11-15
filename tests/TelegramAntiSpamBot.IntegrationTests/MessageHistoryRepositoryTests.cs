
namespace TelegramAntiSpamBot.IntegrationTests;

public class MessageHistoryRepositoryTests
{
    [Theory]
    [InlineData(32767)]
    [InlineData(32768)]
    [InlineData(32769)]
    public async Task SaveEnforcesLimitOnSerializedContent(int serializedLength)
    {
        var table = new HistoryTable { Paging = true };
        using var services = CreateServices(table);
        var repository = services.GetRequiredService<IMessageHistoryRepository>();
        var emptyContent = new HistoryMessageContent(null, null, "");
        var overhead = JsonSerializer.Serialize(emptyContent).Length;
        var message = new HistoryMessage(-100, 7, DateTime.UtcNow.Ticks,
            new(null, null, new string('a', serializedLength - overhead)));

        await repository.SaveAsync(message);

        var row = Assert.Single(table.Rows);
        var storedJson = row.GetString("Content");
        Assert.Equal(Math.Min(serializedLength, 32768), storedJson.Length);
        Assert.InRange(Encoding.Unicode.GetByteCount(storedJson), 0, 65536);
        Assert.Equal(serializedLength > 32768, row.ContainsKey("ContentTruncated"));
        var stored = Assert.Single((await repository.GetPageAsync(-100, 25, null)).Messages);
        Assert.StartsWith(stored.Content.Text, message.Content.Text);
        Assert.Equal(message.Content.Text.Length - Math.Max(0, serializedLength - 32768), stored.Content.Text.Length);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("\u0416")]
    [InlineData("\U0001F600")]
    [InlineData("\"\\\n\t")]
    public async Task OversizedImageAnalysisAndReplyRemainValidAndReadable(string unit)
    {
        var table = new HistoryTable { Paging = true };
        using var services = CreateServices(table);
        var repository = services.GetRequiredService<IMessageHistoryRepository>();
        var text = string.Concat(Enumerable.Repeat(unit, 40000));
        var content = new HistoryMessageContent(new(42, "Alice", "alice"),
            new(6, new(43, "Bob", null), text), new MessagePayload(text, text, "Caption"));
        var message = new HistoryMessage(-100, 7, DateTime.UtcNow.Ticks, content, HistoryUpdateType.EditedMessage, 101);

        await repository.SaveAsync(message);

        var row = Assert.Single(table.Rows);
        Assert.InRange(Encoding.Unicode.GetByteCount(row.GetString("Content")), 0, 65536);
        Assert.True(row.GetBoolean("ContentTruncated"));
        var stored = Assert.Single((await repository.GetPageAsync(-100, 25, null)).Messages);
        Assert.Equal(message with { Content = stored.Content }, stored);
        Assert.Equal(content.From, stored.Content.From);
        Assert.Equal(content.ReplyTo!.From, stored.Content.ReplyTo!.From);
        Assert.Equal(6, stored.Content.ReplyTo.MessageId);
        Assert.Equal("Caption", stored.Content.Payload.UserMessage);
        foreach (var value in new[] { stored.Content.Payload.ImageText!, stored.Content.Payload.ImageDescription!, stored.Content.ReplyTo.Text })
        {
            Assert.NotEmpty(value);
            Assert.True(value.Length < text.Length);
            Assert.StartsWith(value, text);
            Assert.False(char.IsHighSurrogate(value[^1]));
        }
        var range = new List<HistoryMessage>();
        await foreach (var entry in repository.GetAsync(-100, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1)))
            range.Add(entry);
        Assert.Equal(stored, Assert.Single(range));
        Assert.Equal(text, message.Content.Payload.ImageText);
    }

    [Fact]
    public async Task ShortEditReplacesTruncatedContentAndRemovesTruncationFlag()
    {
        var table = new HistoryTable { Paging = true };
        using var services = CreateServices(table);
        var repository = services.GetRequiredService<IMessageHistoryRepository>();
        var message = new HistoryMessage(-100, 7, DateTime.UtcNow.Ticks, new(null, null, new string('a', 40000)));
        await repository.SaveAsync(message);
        var edited = message with { Content = new(null, null, "Short edit"), UpdateType = HistoryUpdateType.EditedMessage };

        await repository.SaveAsync(edited);

        Assert.False(Assert.Single(table.Rows).ContainsKey("ContentTruncated"));
        Assert.Equal(edited, Assert.Single((await repository.GetPageAsync(-100, 25, null)).Messages));
    }

    [Fact]
    public async Task SavePreservesContentAndOrdersOldestFirstWithoutTimestampCollisions()
    {
        var table = new HistoryTable();
        using var services = CreateServices(table);
        var repository = services.GetRequiredService<IMessageHistoryRepository>();
        var content = new HistoryMessageContent(new(42, "Alice", "alice"), new(6, null, "Reply text"), "Text");
        var ticks = DateTime.UtcNow.Ticks;

        await repository.SaveAsync(new(-100, 7, ticks, content));
        await repository.SaveAsync(new(-100, 8, ticks, content));
        await repository.SaveAsync(new(-100, 9, ticks + 1, content));

        Assert.Equal(3, table.Saved.Select(entry => entry.RowKey).Distinct().Count());
        Assert.True(string.CompareOrdinal(table.Saved[2].RowKey, table.Saved[0].RowKey) > 0);
        Assert.Equal(ticks.ToString("D19") + "-0000000007", table.Saved[0].RowKey);
        Assert.All(table.Saved, entry =>
        {
            Assert.Equal("-100", entry.PartitionKey);
            Assert.Equal(content, JsonSerializer.Deserialize<HistoryMessageContent>(entry.GetString("Content")));
        });
        Assert.Equal(ticks, table.Saved[0].GetInt64("DateTimeTicks"));
    }

    [Theory]
    [InlineData(HistoryUpdateType.Message, HistoryUpdateType.EditedMessage)]
    [InlineData(HistoryUpdateType.ChannelPost, HistoryUpdateType.EditedChannelPost)]
    public async Task SaveAndReadReplaceOriginalWithEditedPayload(HistoryUpdateType originalType,
        HistoryUpdateType editedType)
    {
        var table = new HistoryTable { Paging = true };
        using var services = CreateServices(table);
        var repository = services.GetRequiredService<IMessageHistoryRepository>();
        var ticks = DateTime.UtcNow.Ticks;
        var payload = new MessagePayload("A poster", "Full text\nSecond line", "Caption");
        var original = new HistoryMessage(-100, 7, ticks, new(null, null, payload), originalType, 100);
        var edited = original with
        {
            UpdateType = editedType,
            UpdateId = 101,
            Content = new(null, null, payload with { UserMessage = "Edited caption" })
        };
        await repository.SaveAsync(original);
        await repository.SaveAsync(edited);

        Assert.Equal(table.Saved[0].RowKey, table.Saved[1].RowKey);
        Assert.Equal(ticks.ToString("D19") + "-0000000007", table.Saved[1].RowKey);
        Assert.Single(table.Rows);
        using var content = JsonDocument.Parse(table.Saved[1].GetString("Content"));
        Assert.False(content.RootElement.TryGetProperty("Text", out _));
        Assert.False(content.RootElement.TryGetProperty("UserMessageCount", out _));
        Assert.Equal("Full text\nSecond line", content.RootElement.GetProperty("Payload").GetProperty("ImageText").GetString());
        Assert.Equal(editedType.ToString(), table.Saved[1].GetString("UpdateType"));
        var page = await repository.GetPageAsync(-100, 25, null);
        Assert.Equal(edited, Assert.Single(page.Messages));
        var range = new List<HistoryMessage>();
        await foreach (var message in repository.GetAsync(-100, new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc)),
            new DateTimeOffset(new DateTime(ticks + TimeSpan.TicksPerHour, DateTimeKind.Utc))))
            range.Add(message);
        Assert.Equal(edited, Assert.Single(range));
    }

    [Fact]
    public async Task RepeatedEditsAndRetriesUpsertSameRow()
    {
        var table = new HistoryTable { Paging = true };
        using var services = CreateServices(table);
        var repository = services.GetRequiredService<IMessageHistoryRepository>();
        var first = new HistoryMessage(-100, 7, DateTime.UtcNow.Ticks,
            new(null, null, new MessagePayload(UserMessage: "First edit")), HistoryUpdateType.EditedMessage, 100);
        var second = first with
        {
            UpdateId = 101,
            Content = new(null, null, new MessagePayload(UserMessage: "Second edit"))
        };
        await repository.SaveAsync(first);
        await repository.SaveAsync(second);
        await repository.SaveAsync(second);

        Assert.Equal(table.Saved[0].RowKey, table.Saved[1].RowKey);
        Assert.Equal(table.Saved[1].RowKey, table.Saved[2].RowKey);
        Assert.Single(table.Rows);
        var page = await repository.GetPageAsync(-100, 25, null);
        Assert.Equal(second, Assert.Single(page.Messages));
    }

    [Fact]
    public async Task LegacyHistoryTextIsReadAsTypedUserMessage()
    {
        var table = new HistoryTable { Paging = true };
        var ticks = DateTime.UtcNow.Ticks;
        table.Rows.Add(new TableEntity("-100", ticks.ToString("D19") + "-0000000007")
        {
            ["DateTimeTicks"] = ticks,
            ["Content"] = "{\"From\":null,\"ReplyTo\":null,\"Text\":\"Legacy text\"}"
        });
        using var services = CreateServices(table);
        var repository = services.GetRequiredService<IMessageHistoryRepository>();
        var message = Assert.Single((await repository.GetPageAsync(-100, 25, null)).Messages);
        Assert.Equal(new MessagePayload(UserMessage: "Legacy text"), message.Content.Payload);
        Assert.Equal(HistoryUpdateType.Message, message.UpdateType);
        var range = new List<HistoryMessage>();
        await foreach (var row in repository.GetAsync(-100, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1)))
            range.Add(row);
        Assert.Equal(message, Assert.Single(range));
    }

    [Fact]
    public async Task TrimKeepsLatestThousandPerChatAndBatchesDeletesByPartition()
    {
        var table = new HistoryTable();
        foreach (var partition in new[] { "-100", "-200", "-300" })
        {
            var count = partition == "-300" ? 20 : 1205;
            for (var index = 0; index < count; index++)
                table.Rows.Add(new TableEntity(partition, index.ToString("D19")));
        }
        using var services = CreateServices(table);

        var deleted = await services.GetRequiredService<IMessageHistoryRepository>().TrimAsync();

        Assert.Equal(410, deleted);
        Assert.All(table.Batches, batch =>
        {
            Assert.InRange(batch.Length, 1, 100);
            Assert.Single(batch.Select(action => action.Entity.PartitionKey).Distinct());
            Assert.All(batch, action =>
            {
                Assert.Equal(TableTransactionActionType.Delete, action.ActionType);
                Assert.InRange(long.Parse(action.Entity.RowKey), 0, 204);
                Assert.Equal(ETag.All, action.ETag);
            });
        });
        Assert.Equal(410, table.Batches.SelectMany(batch => batch)
            .Select(action => (action.Entity.PartitionKey, action.Entity.RowKey)).Distinct().Count());
    }

    [Fact]
    public async Task GetUsesPartitionAndRowKeyRangeWithInclusiveStartAndExclusiveEnd()
    {
        var table = new HistoryTable();
        using var services = CreateServices(table);
        var repository = services.GetRequiredService<IMessageHistoryRepository>();
        var from = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.FromHours(2));
        var to = from.AddHours(1);
        var content = new HistoryMessageContent(new(42, "Alice", null), null, "Text");
        var message = new HistoryMessage(-100, 7, from.UtcDateTime.Ticks, content);
        await repository.SaveAsync(message);

        var messages = new List<HistoryMessage>();
        await foreach (var entry in repository.GetAsync(-100, from, to))
            messages.Add(entry);

        Assert.Equal(message, Assert.Single(messages));
        var start = from.UtcDateTime.Ticks.ToString("D19") + "-";
        var end = to.UtcDateTime.Ticks.ToString("D19") + "-";
        Assert.Equal(TableClient.CreateQueryFilter($"PartitionKey eq {"-100"} and RowKey ge {start} and RowKey lt {end}"),
            table.Filter);
        Assert.True(string.CompareOrdinal(start, table.Saved[0].RowKey) < 0);
        Assert.True(string.CompareOrdinal(end, to.UtcDateTime.Ticks.ToString("D19") + "-0000000007") < 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-100L)]
    public async Task PagedHistoryPreservesAuthorReplyAndText(long? chatId)
    {
        var table = new HistoryTable { Paging = true, ResultToken = "next" };
        using var services = CreateServices(table);
        var repository = services.GetRequiredService<IMessageHistoryRepository>();
        var message = new HistoryMessage(-100, 7, DateTime.UtcNow.Ticks,
            new(new(42, "Alice", "alice"), new(6, new(43, "Bob", null), "Quoted"), "Original"));
        await repository.SaveAsync(message);
        var page = await repository.GetPageAsync(chatId, 25, "previous");
        Assert.Equal(message, Assert.Single(page.Messages));
        Assert.Equal("next", page.ContinuationToken);
        Assert.Equal("previous", table.RequestedToken);
        Assert.Equal(25, table.RequestedPageSize);
        Assert.Equal(chatId is null ? null : TableClient.CreateQueryFilter($"PartitionKey eq {"-100"}"), table.Filter);
    }

    [Fact]
    public async Task MissingHistoryTableListsAsEmpty()
    {
        using var services = CreateServices(new HistoryTable { Missing = true });
        var page = await services.GetRequiredService<IMessageHistoryRepository>().GetPageAsync(null, 100, null);
        Assert.Empty(page.Messages);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task RangePagesUseIndexedUtcBoundsAndReadOnlyOneHundredRowsPerCall()
    {
        var table = new HistoryTable();
        using var services = CreateServices(table);
        var repository = services.GetRequiredService<IMessageHistoryRepository>();
        var from = new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.FromHours(2));
        var to = from.AddSeconds(105);
        for (var id = -1; id <= 105; id++)
        {
            var message = new HistoryMessage(-100, id + 2, from.AddSeconds(id).UtcTicks,
                new(null, null, "Message " + id));
            await repository.SaveAsync(message);
            await repository.SaveAsync(message with { ChatId = -200 });
        }
        using var cancellation = new CancellationTokenSource();

        var first = await repository.GetRangePageAsync(-100, from, to, cancellationToken: cancellation.Token);
        var second = await repository.GetRangePageAsync(-100, from, to, first.ContinuationToken, cancellation.Token);

        Assert.Equal(Enumerable.Range(2, 100), first.Messages.Select(message => message.MessageId));
        Assert.Equal(Enumerable.Range(102, 5), second.Messages.Select(message => message.MessageId));
        Assert.All(first.Messages.Concat(second.Messages), message => Assert.Equal(-100, message.ChatId));
        Assert.Equal("100", first.ContinuationToken);
        Assert.Null(second.ContinuationToken);
        Assert.Equal(2, table.RangePagesRead);
        Assert.Equal(100, table.MaxPerPage);
        Assert.Equal(100, table.RequestedPageSize);
        Assert.Equal(cancellation.Token, table.QueryCancellationToken);
        var start = from.UtcTicks.ToString("D19") + "-";
        var end = to.UtcTicks.ToString("D19") + "-";
        Assert.Equal(TableClient.CreateQueryFilter($"PartitionKey eq {"-100"} and RowKey ge {start} and RowKey lt {end}"), table.Filter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RangePagesRejectInvalidIntervalBeforeQuerying(int seconds)
    {
        var table = new HistoryTable();
        using var services = CreateServices(table);
        var from = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<ArgumentException>(() => services.GetRequiredService<IMessageHistoryRepository>()
            .GetRangePageAsync(-100, from, from.AddSeconds(seconds)));
        Assert.Null(table.Filter);
    }

    [Fact]
    public async Task RangePageStorageFailureIsNotReturnedAsEmptyHistory()
    {
        using var services = CreateServices(new HistoryTable { Missing = true });
        await Assert.ThrowsAsync<RequestFailedException>(() => services.GetRequiredService<IMessageHistoryRepository>()
            .GetRangePageAsync(-100, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow));
    }

    private static ServiceProvider CreateServices(HistoryTable table) => new ServiceCollection()
        .AddLogging()
        .AddPersistence()
        .AddKeyedSingleton<TableClient>("MessageHistory", table)
        .BuildServiceProvider();

    [Fact]
    public async Task ClearChatDeletesAllItsRowsInBatchesAndPreservesOtherChats()
    {
        var table = new HistoryTable();
        for (var index = 0; index < 205; index++)
            table.Rows.Add(new TableEntity("-100", index.ToString("D19")));
        table.Rows.Add(new TableEntity("-200", "other-message"));
        using var services = CreateServices(table);
        using var cancellation = new CancellationTokenSource();

        var deleted = await services.GetRequiredService<IMessageHistoryRepository>().ClearChatAsync(-100, cancellation.Token);

        Assert.Equal(205, deleted);
        Assert.Equal("PartitionKey eq '-100'", table.Filter);
        Assert.Equal(cancellation.Token, table.QueryCancellationToken);
        Assert.Equal(cancellation.Token, table.TransactionCancellationToken);
        Assert.Equal(new[] { 100, 100, 5 }, table.Batches.Select(batch => batch.Length));
        var actions = table.Batches.SelectMany(batch => batch).ToArray();
        Assert.Equal(205, actions.Select(action => action.Entity.RowKey).Distinct().Count());
        Assert.All(actions, action =>
        {
            Assert.Equal("-100", action.Entity.PartitionKey);
            Assert.Equal(TableTransactionActionType.Delete, action.ActionType);
            Assert.Equal(ETag.All, action.ETag);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearEmptyOrMissingHistoryDoesNotSubmitDeletes(bool missing)
    {
        var table = new HistoryTable { Missing = missing };
        using var services = CreateServices(table);
        Assert.Equal(0, await services.GetRequiredService<IMessageHistoryRepository>().ClearChatAsync(-100));
        Assert.Empty(table.Batches);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(503)]
    public async Task ClearChatPropagatesTransactionFailures(int status)
    {
        var table = new HistoryTable { TransactionError = status };
        table.Rows.Add(new TableEntity("-100", "message"));
        using var services = CreateServices(table);
        await Assert.ThrowsAsync<RequestFailedException>(() => services.GetRequiredService<IMessageHistoryRepository>().ClearChatAsync(-100));
    }

    private sealed class HistoryTable : TableClient
    {
        public List<TableEntity> Saved { get; } = [];
        public List<TableEntity> Rows { get; } = [];
        public List<TableTransactionAction[]> Batches { get; } = [];
        public string? Filter { get; private set; }
        public bool Paging { get; init; }
        public bool Missing { get; init; }
        public string? ResultToken { get; init; }
        public string? RequestedToken { get; private set; }
        public int? RequestedPageSize { get; private set; }
        public int? MaxPerPage { get; private set; }
        public int RangePagesRead { get; private set; }
        public CancellationToken QueryCancellationToken { get; private set; }
        public CancellationToken TransactionCancellationToken { get; private set; }
        public int? TransactionError { get; init; }

        public override Task<Response> UpsertEntityAsync<T>(T entity,
            TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
        {
            Assert.Equal(TableUpdateMode.Replace, mode);
            var row = (TableEntity)(object)entity;
            Assert.All(row.Select(property => property.Value).OfType<string>(), value => Assert.InRange(Encoding.Unicode.GetByteCount(value), 0, 65536));
            Saved.Add(row);
            Rows.RemoveAll(existing => existing.PartitionKey == row.PartitionKey && existing.RowKey == row.RowKey);
            Rows.Add(row);
            return Task.FromResult<Response>(null!);
        }

        public override AsyncPageable<T> QueryAsync<T>(string? filter = null, int? maxPerPage = null,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            Filter = filter;
            MaxPerPage = maxPerPage;
            QueryCancellationToken = cancellationToken;
            if (Missing)
                throw new RequestFailedException(404, "Table not found");
            if (maxPerPage == 100)
            {
                Assert.Equal(new[] { "PartitionKey", "RowKey", "DateTimeTicks", "Content", "UpdateType", "UpdateId" }, select);
                var bounds = System.Text.RegularExpressions.Regex.Match(filter!,
                    "^PartitionKey eq '([^']+)' and RowKey ge '([^']+)' and RowKey lt '([^']+)'$");
                Assert.True(bounds.Success);
                var rows = Rows.Where(row => row.PartitionKey == bounds.Groups[1].Value &&
                    string.CompareOrdinal(row.RowKey, bounds.Groups[2].Value) >= 0 &&
                    string.CompareOrdinal(row.RowKey, bounds.Groups[3].Value) < 0).OrderBy(row => row.RowKey).Cast<T>().ToArray();
                return new RangePageable<T>(rows, (token, size) =>
                {
                    RequestedToken = token;
                    RequestedPageSize = size;
                    RangePagesRead++;
                });
            }
            if ((filter is null && !Paging) || select is not null)
                Assert.Equal(new[] { "PartitionKey", "RowKey" }, select);
            else
                Assert.Null(select);
            var partitionFilter = filter is null ? null : System.Text.RegularExpressions.Regex.Match(filter, "^PartitionKey eq '([^']+)'$");
            var selectedRows = partitionFilter is { Success: true }
                ? Rows.Where(row => row.PartitionKey == partitionFilter.Groups[1].Value)
                : Rows;
            return new TestPageable<T>(Page<T>.FromValues(selectedRows.Cast<T>().ToArray(), ResultToken, null!),
                (token, size) => { RequestedToken = token; RequestedPageSize = size; });
        }

        public override Task<Response<IReadOnlyList<Response>>> SubmitTransactionAsync(
            IEnumerable<TableTransactionAction> transactionActions, CancellationToken cancellationToken = default)
        {
            TransactionCancellationToken = cancellationToken;
            if (TransactionError is { } status)
                throw new RequestFailedException(status, "Transaction failed");
            Batches.Add(transactionActions.ToArray());
            return Task.FromResult<Response<IReadOnlyList<Response>>>(null!);
        }

        private sealed class RangePageable<T>(T[] rows, Action<string?, int?> onPage) : AsyncPageable<T> where T : notnull
        {
            public override async IAsyncEnumerable<Page<T>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
            {
                await Task.CompletedTask;
                onPage(continuationToken, pageSizeHint);
                var offset = continuationToken is null ? 0 : int.Parse(continuationToken);
                var values = rows.Skip(offset).Take(pageSizeHint!.Value).ToArray();
                var next = offset + values.Length;
                yield return Page<T>.FromValues(values, next < rows.Length ? next.ToString() : null, null!);
                throw new InvalidOperationException("A single history tool call must not read a second server page.");
            }
        }
    }
}