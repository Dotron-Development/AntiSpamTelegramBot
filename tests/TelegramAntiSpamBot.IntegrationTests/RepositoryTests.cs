
namespace TelegramAntiSpamBot.IntegrationTests;

public class RepositoryTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(-100L, "PartitionKey eq '-100'")]
    [InlineData(100L, "PartitionKey eq '100'")]
    public async Task ReviewQueryPreservesFilterPageSizeTokenAndAzureOrder(long? chatId, string? expectedFilter)
    {
        var table = new TableStub
        {
            QueryRows = [MessageRow("-200", "9"), MessageRow("-100", "7")]
        };
        using var services = Services("ProbablySpam", table);

        var page = await services.GetRequiredService<IProbablySpamRepository>()
            .GetReviewMessagesAsync(chatId, 12, "opaque");

        Assert.Equal(expectedFilter, table.Filter);
        Assert.Null(table.QueryMaxPerPage);
        Assert.Equal("opaque", table.ContinuationToken);
        Assert.Equal(12, table.PageSizeHint);
        Assert.Equal(1, table.PagesRead);
        Assert.Equal("next", page.ContinuationToken);
        Assert.Equal(new long[] { -200, -100 }, page.Messages.Select(message => message.ChatId));
        Assert.Equal(new[] { 9, 7 }, page.Messages.Select(message => message.MessageId));
        Assert.All(page.Messages, message =>
        {
            Assert.Equal(42, message.UserId);
            Assert.Equal(new ReviewMessageFrom("Alice", "alice"), message.From);
            Assert.Equal(new ReviewMessageReplyTo(6, new ReviewMessageFrom("Bob", "bob"), "Question"), message.ReplyTo);
            Assert.Equal("caption", message.Message);
            Assert.Equal("OCR", message.ImageText);
            Assert.Equal("HASH", message.ContentHash);
            Assert.Equal(638000000000000000, message.MessageDateTicks);
        });
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"messageId\":0}")]
    public async Task ReviewIgnoresInvalidReplyContext(string json)
    {
        var row = MessageRow("-100", "7");
        row["ReplyTo"] = json;
        var table = new TableStub { LookupRow = row };
        using var services = Services("ProbablySpam", table);

        var message = await services.GetRequiredService<IProbablySpamRepository>().GetReviewMessageAsync(-100, 7);

        Assert.NotNull(message);
        Assert.Null(message.ReplyTo);
    }

    [Fact]
    public async Task ReviewQueryWithoutPagesReturnsEmptyPage()
    {
        var table = new TableStub { NoPages = true };
        using var services = Services("ProbablySpam", table);

        var page = await services.GetRequiredService<IProbablySpamRepository>()
            .GetReviewMessagesAsync(null, 100, null);

        Assert.Empty(page.Messages);
        Assert.Null(page.ContinuationToken);
    }

    [Fact]
    public async Task ReviewSaveLookupAndDeletePreserveKeysAndReplaceMode()
    {
        var table = new TableStub();
        using var services = Services("ProbablySpam", table);
        var repository = services.GetRequiredService<IProbablySpamRepository>();
        var message = new ReviewMessage(-100, 7, 42, new ReviewMessageFrom("Alice", "alice"),
            new ReviewMessageReplyTo(6, new ReviewMessageFrom("Bob", "bob"), "Question"),
            "caption", "OCR", "HASH", 638000000000000000);

        await repository.SaveReviewMessageAsync(message);
        var write = Assert.Single(table.Writes);
        Assert.Equal("-100", write.Entity.PartitionKey);
        Assert.Equal("7", write.Entity.RowKey);
        Assert.Equal(TableUpdateMode.Replace, write.Mode);
        Assert.False(write.Update);
        Assert.Equal("{\"name\":\"Alice\",\"nick\":\"alice\"}", Property<string>(write.Entity, "From"));
        Assert.Equal("{\"messageId\":6,\"from\":{\"name\":\"Bob\",\"nick\":\"bob\"},\"text\":\"Question\"}",
            Property<string>(write.Entity, "ReplyTo"));
        table.LookupRow = MessageRow("-100", "7");
        Assert.Equal(message, await repository.GetReviewMessageAsync(-100, 7));
        Assert.Equal(("-100", "7"), table.LookupKeys);
        table.LookupRow = null;
        Assert.Null(await repository.GetReviewMessageAsync(-100, 7));
        await repository.DeleteReviewMessageAsync(-100, 7);
        Assert.Equal(("-100", "7"), table.DeletedKeys);
        Assert.Equal(default, table.DeleteEtag);
    }

    [Fact]
    public async Task ChatQueryUsesCatalogPartitionAndForwardsPagination()
    {
        var lastSeen = DateTimeOffset.UtcNow;
        var table = new TableStub
        {
            QueryRows = [new() { ["PartitionKey"] = "chats", ["RowKey"] = "-100", ["Name"] = "Test", ["LastSeenUtc"] = lastSeen }]
        };
        using var services = Services("Chats", table);

        var page = await services.GetRequiredService<IChatsRepository>().GetReviewChatsAsync(12, "opaque");

        Assert.Equal("PartitionKey eq 'chats'", table.Filter);
        Assert.Equal("opaque", table.ContinuationToken);
        Assert.Equal(12, table.PageSizeHint);
        Assert.Equal(new ReviewChat(-100, "Test", lastSeen), Assert.Single(page.Chats));
        Assert.Equal("next", page.ContinuationToken);
    }

    [Fact]
    public async Task ChatSaveCreatesMissingTableAndRetriesReplace()
    {
        var table = new TableStub { FailFirstUpsert = true };
        using var services = Services("Chats", table);
        var chat = new ReviewChat(-100, "Test", DateTimeOffset.UtcNow);

        await services.GetRequiredService<IChatsRepository>().SaveChatAsync(chat);

        Assert.Equal(1, table.CreateCalls);
        Assert.Equal(2, table.UpsertCalls);
        var write = Assert.Single(table.Writes);
        Assert.Equal("chats", write.Entity.PartitionKey);
        Assert.Equal("-100", write.Entity.RowKey);
        Assert.Equal(chat.Name, Property<string>(write.Entity, "Name"));
        Assert.Equal(chat.LastSeenUtc, Property<DateTimeOffset>(write.Entity, "LastSeenUtc"));
        Assert.Equal(TableUpdateMode.Replace, write.Mode);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    public async Task ChatListOnlyTreatsMissingTableAsEmpty(int status)
    {
        var table = new TableStub { QueryError = status };
        using var services = Services("Chats", table);
        var repository = services.GetRequiredService<IChatsRepository>();

        if (status == 404)
        {
            var page = await repository.GetReviewChatsAsync(100, null);
            Assert.Empty(page.Chats);
            Assert.Null(page.ContinuationToken);
        }
        else
        {
            await Assert.ThrowsAsync<RequestFailedException>(() => repository.GetReviewChatsAsync(100, null));
        }
    }

    [Theory]
    [InlineData(null, false, 1)]
    [InlineData(3, true, 4)]
    public async Task MessageCountPreservesMissingAndExistingEntryBehavior(int? count, bool update, int expected)
    {
        var table = new TableStub { LookupRow = count is { } value ? new() { ["Count"] = value } : null };
        using var services = Services("MessageCount", table);
        var repository = services.GetRequiredService<IMessageCountRepository>();

        Assert.Equal(count ?? 0, await repository.GetUserMessageCount(-100, 42));
        await repository.IncreaseMessageCount(-100, 42);

        Assert.Equal(("-100", "42"), table.LookupKeys);
        var write = Assert.Single(table.Writes);
        Assert.Equal(update, write.Update);
        Assert.Equal(TableUpdateMode.Merge, write.Mode);
        Assert.Equal(expected, Property<int>(write.Entity, "Count"));
        Assert.Equal("-100", write.Entity.PartitionKey);
        Assert.Equal("42", write.Entity.RowKey);
        if (update) Assert.Equal(ETag.All, write.Etag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SpamHashPreservesShortcutAndReplaceBehavior(bool? isSpam)
    {
        var table = new TableStub { LookupRow = isSpam is { } value ? new() { ["IsSpam"] = value } : null };
        using var services = Services("SpamHash", table);
        var repository = services.GetRequiredService<ISpamHashRepository>();

        var result = await repository.IsSpam("HASH");
        Assert.Equal(isSpam.HasValue, result.IsShortCut);
        Assert.Equal(isSpam, result.IsSpam);
        Assert.Equal(("1", "HASH"), table.LookupKeys);
        await repository.SaveMessageHash("HASH", false);
        var write = Assert.Single(table.Writes);
        Assert.Equal("1", write.Entity.PartitionKey);
        Assert.Equal("HASH", write.Entity.RowKey);
        Assert.False(Property<bool>(write.Entity, "IsSpam"));
        Assert.Equal(TableUpdateMode.Replace, write.Mode);
    }

    [Fact]
    public async Task SpamStatsPreservesDefaultsCountersAndTimingCalculation()
    {
        var table = new TableStub();
        using var services = Services("SpamStats", table);
        var repository = services.GetRequiredService<ISpamStatsRepository>();

        Assert.Equal(0, (await repository.GetDailyStats(-100)).Value);
        Assert.Equal(0, (await repository.GetMonthlyStats(-100)).Value);
        Assert.Null((await repository.GetTimingsStats(-100)).Value.MessageCount);
        await repository.UpdateChannelSpamStats(-100, 20);
        Assert.Equal(3, table.Writes.Count);
        Assert.All(table.Writes, write => Assert.Equal("-100", write.Entity.PartitionKey));
        var daily = Assert.Single(table.Writes, write => write.Entity.RowKey.StartsWith("DAILY_MESSAGES_DELETED_"));
        var monthly = Assert.Single(table.Writes, write => write.Entity.RowKey.StartsWith("MONTHLY_MESSAGES_DELETED_"));
        Assert.Equal("1", Property<string>(daily.Entity, "Value"));
        Assert.Equal("1", Property<string>(monthly.Entity, "Value"));
        Assert.Equal(TableUpdateMode.Merge, daily.Mode);
        Assert.Equal(TableUpdateMode.Merge, monthly.Mode);
        Assert.False(daily.Update);
        Assert.False(monthly.Update);
        Assert.Equal(TableUpdateMode.Replace, Assert.Single(table.Writes, write => write.Entity.RowKey == "TIMINGS").Mode);

        table.StoreWrites = true;
        foreach (var write in table.Writes) table.StoredRows[write.Entity.RowKey] = new() { ["Value"] = Property<string>(write.Entity, "Value") };
        table.Writes.Clear();
        await repository.UpdateChannelSpamStats(-100, 40);

        Assert.Equal(2, (await repository.GetDailyStats(-100)).Value);
        Assert.Equal(2, (await repository.GetMonthlyStats(-100)).Value);
        var timings = (await repository.GetTimingsStats(-100)).Value;
        Assert.Equal(2, timings.MessageCount);
        Assert.Equal(30, timings.AvgDelayMs);
        Assert.Equal(20, timings.MinDelayMs);
        Assert.Equal(40, timings.MaxDelayMs);
        Assert.All(table.Writes.Where(write => write.Entity.RowKey != "TIMINGS"), write =>
        {
            Assert.True(write.Update);
            Assert.Equal(ETag.All, write.Etag);
        });
    }

    private static ServiceProvider Services(string tableName, TableClient table) => new ServiceCollection()
        .AddPersistence()
        .AddKeyedSingleton(tableName, table)
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });

    private static T Property<T>(object entity, string name) => (T)entity.GetType().GetProperty(name)!.GetValue(entity)!;

    private static Dictionary<string, object> MessageRow(string chatId, string messageId) => new()
    {
        ["PartitionKey"] = chatId, ["RowKey"] = messageId, ["UserId"] = 42L,
        ["From"] = "{\"name\":\"Alice\",\"nick\":\"alice\"}",
        ["ReplyTo"] = "{\"messageId\":6,\"from\":{\"name\":\"Bob\",\"nick\":\"bob\"},\"text\":\"Question\"}",
        ["Message"] = "caption", ["ImageText"] = "OCR", ["ContentHash"] = "HASH", ["MessageDateTicks"] = 638000000000000000L
    };

    private sealed class TableStub : TableClient
    {
        public Dictionary<string, object>[] QueryRows { get; init; } = [];
        public Dictionary<string, object>? LookupRow { get; set; }
        public Dictionary<string, Dictionary<string, object>> StoredRows { get; } = [];
        public bool StoreWrites { get; set; }
        public bool NoPages { get; init; }
        public int? QueryError { get; init; }
        public bool FailFirstUpsert { get; init; }
        public string? Filter { get; private set; }
        public int? QueryMaxPerPage { get; private set; }
        public string? ContinuationToken { get; set; }
        public int? PageSizeHint { get; set; }
        public int PagesRead { get; set; }
        public int UpsertCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public (string, string)? LookupKeys { get; private set; }
        public (string, string)? DeletedKeys { get; private set; }
        public ETag DeleteEtag { get; private set; }
        public List<(ITableEntity Entity, bool Update, TableUpdateMode Mode, ETag Etag)> Writes { get; } = [];

        public override AsyncPageable<T> QueryAsync<T>(Expression<Func<T, bool>> filter, int? maxPerPage = null,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default) =>
            QueryAsync<T>(TableClient.CreateQueryFilter(filter), maxPerPage, select, cancellationToken);

        public override AsyncPageable<T> QueryAsync<T>(string? filter = null, int? maxPerPage = null,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            Filter = filter;
            QueryMaxPerPage = maxPerPage;
            if (QueryError is { } status) throw new RequestFailedException(status, "Query failed");
            return new Pages<T>(this, QueryRows.Select(CreateEntity<T>).ToArray());
        }

        public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(string partitionKey, string rowKey,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            LookupKeys = (partitionKey, rowKey);
            var row = StoredRows.GetValueOrDefault(rowKey) ?? LookupRow;
            return Task.FromResult<NullableResponse<T>>(new OptionalResponse<T>(row is null ? null : CreateEntity<T>(row)));
        }

        public override Task<Response> UpsertEntityAsync<T>(T entity, TableUpdateMode mode = TableUpdateMode.Merge,
            CancellationToken cancellationToken = default)
        {
            UpsertCalls++;
            if (FailFirstUpsert && UpsertCalls == 1) throw new RequestFailedException(404, "Table missing");
            Save(entity, false, mode, default);
            return Task.FromResult<Response>(new RawResponse());
        }

        public override Task<Response> UpdateEntityAsync<T>(T entity, ETag ifMatch, TableUpdateMode mode = TableUpdateMode.Merge,
            CancellationToken cancellationToken = default)
        {
            Save(entity, true, mode, ifMatch);
            return Task.FromResult<Response>(new RawResponse());
        }

        private void Save(ITableEntity entity, bool update, TableUpdateMode mode, ETag etag)
        {
            Writes.Add((entity, update, mode, etag));
            if (StoreWrites) StoredRows[entity.RowKey] = new() { ["Value"] = Property<string>(entity, "Value") };
        }

        public override Task<Response<TableItem>> CreateIfNotExistsAsync(CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(Response.FromValue(new TableItem("Chats"), new RawResponse()));
        }

        public override Task<Response> DeleteEntityAsync(string partitionKey, string rowKey, ETag ifMatch = default,
            CancellationToken cancellationToken = default)
        {
            DeletedKeys = (partitionKey, rowKey);
            DeleteEtag = ifMatch;
            return Task.FromResult<Response>(new RawResponse());
        }

        private static T CreateEntity<T>(Dictionary<string, object> row)
        {
            var entity = Activator.CreateInstance<T>();
            foreach (var (name, value) in row) typeof(T).GetProperty(name)!.SetValue(entity, value);
            return entity;
        }
    }

    private sealed class Pages<T>(TableStub table, T[] values) : AsyncPageable<T> where T : notnull
    {
        public override async IAsyncEnumerable<Page<T>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
        {
            table.ContinuationToken = continuationToken;
            table.PageSizeHint = pageSizeHint;
            await Task.CompletedTask;
            if (table.NoPages) yield break;
            table.PagesRead++;
            yield return Page<T>.FromValues(values, "next", new RawResponse());
            table.PagesRead++;
            yield return Page<T>.FromValues([], null, new RawResponse());
        }
    }

    private sealed class OptionalResponse<T>(T? value) : NullableResponse<T> where T : class
    {
        public override bool HasValue => value is not null;
        public override T Value => value!;
        public override Response GetRawResponse() => new RawResponse();
    }

    private sealed class RawResponse : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = string.Empty;
        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
        protected override bool TryGetHeader(string name, out string value) { value = string.Empty; return false; }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values) { values = []; return false; }
    }
}
