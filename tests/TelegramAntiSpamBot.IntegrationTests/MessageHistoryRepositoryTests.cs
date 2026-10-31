using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using TelegramAntiSpamBot.Persistence;
using TelegramAntiSpamBot.Persistence.DTO;
using TelegramAntiSpamBot.Persistence.Repositories;

namespace TelegramAntiSpamBot.IntegrationTests;

public class MessageHistoryRepositoryTests
{
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
        table.Rows.Add(table.Saved[0]);

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
        table.Rows.Add(table.Saved[0]);
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

    private static ServiceProvider CreateServices(HistoryTable table) => new ServiceCollection()
        .AddPersistence()
        .AddKeyedSingleton<TableClient>("MessageHistory", table)
        .BuildServiceProvider();

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

        public override Task<Response> UpsertEntityAsync<T>(T entity,
            TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
        {
            Saved.Add((TableEntity)(object)entity);
            return Task.FromResult<Response>(null!);
        }

        public override AsyncPageable<T> QueryAsync<T>(string? filter = null, int? maxPerPage = null,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            Filter = filter;
            if (Missing)
                throw new RequestFailedException(404, "Table not found");
            if (filter is null && !Paging)
                Assert.Equal(new[] { "PartitionKey", "RowKey" }, select);
            else
                Assert.Null(select);
            return new TestPageable<T>(Page<T>.FromValues(Rows.Cast<T>().ToArray(), ResultToken, null!),
                (token, size) => { RequestedToken = token; RequestedPageSize = size; });
        }

        public override Task<Response<IReadOnlyList<Response>>> SubmitTransactionAsync(
            IEnumerable<TableTransactionAction> transactionActions, CancellationToken cancellationToken = default)
        {
            Batches.Add(transactionActions.ToArray());
            return Task.FromResult<Response<IReadOnlyList<Response>>>(null!);
        }
    }
}