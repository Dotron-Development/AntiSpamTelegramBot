using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Extensions.DependencyInjection;
using TelegramAntiSpamBot.Persistence;
using TelegramAntiSpamBot.Persistence.Repositories;

namespace TelegramAntiSpamBot.IntegrationTests;

public class CommandCallsRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FirstCallStoresChatCommandAndUtcLastCall()
    {
        var table = new CallsTable();
        using var services = CreateServices(table);

        Assert.True(await services.GetRequiredService<ICommandCallsRepository>()
            .TryAcquireAsync(-100, "/stat", Now.ToOffset(TimeSpan.FromHours(2)), TimeSpan.FromMinutes(5)));

        Assert.Equal("-100", table.Saved!.PartitionKey);
        Assert.Equal("%2Fstat", table.Saved.RowKey);
        Assert.Equal(Now, table.Saved.GetDateTimeOffset("lastCall"));
        Assert.Equal(TimeSpan.Zero, table.Saved.GetDateTimeOffset("lastCall")!.Value.Offset);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(299, false)]
    [InlineData(300, true)]
    [InlineData(301, true)]
    public async Task CooldownAllowsOnlyCallsAtLeastFiveMinutesApart(int elapsedSeconds, bool allowed)
    {
        var table = new CallsTable { Existing = Entry(Now.AddSeconds(-elapsedSeconds)) };
        using var services = CreateServices(table);

        Assert.Equal(allowed, await services.GetRequiredService<ICommandCallsRepository>()
            .TryAcquireAsync(-100, "/stat", Now, TimeSpan.FromMinutes(5)));

        Assert.Equal(allowed, table.Saved is not null);
        if (allowed)
        {
            Assert.Equal(table.Existing.ETag, table.UpdateETag);
            Assert.NotEqual(ETag.All, table.UpdateETag);
            Assert.Equal(Now, table.Saved!.GetDateTimeOffset("lastCall"));
        }
    }

    [Theory]
    [InlineData(false, 409)]
    [InlineData(true, 412)]
    public async Task ConcurrentWinnerIsSilentlyRejected(bool exists, int status)
    {
        var table = new CallsTable
        {
            Existing = exists ? Entry(Now.AddMinutes(-5)) : null,
            WriteFailure = status
        };
        using var services = CreateServices(table);

        Assert.False(await services.GetRequiredService<ICommandCallsRepository>()
            .TryAcquireAsync(-100, "/stat", Now, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task MissingTableIsCreatedBeforeRetryingInsert()
    {
        var table = new CallsTable { MissingTable = true };
        using var services = CreateServices(table);

        Assert.True(await services.GetRequiredService<ICommandCallsRepository>().TryAcquireAsync(-100, "/stat", Now, TimeSpan.FromMinutes(5)));
        Assert.Equal(1, table.Created);
        Assert.NotNull(table.Saved);
    }

    [Theory]
    [InlineData(-200, "/stat")]
    [InlineData(-100, "/chronicle")]
    public async Task DifferentChatsAndCommandsUseSeparateRecords(long chatId, string command)
    {
        var table = new CallsTable { Existing = Entry(Now) };
        using var services = CreateServices(table);

        Assert.True(await services.GetRequiredService<ICommandCallsRepository>().TryAcquireAsync(chatId, command, Now, TimeSpan.FromMinutes(5)));
        Assert.Equal(chatId.ToString(), table.Saved!.PartitionKey);
        Assert.Equal(Uri.EscapeDataString(command), table.Saved.RowKey);
    }

    [Theory]
    [InlineData("/chronicle", "%2Fchronicle")]
    [InlineData("/stat", "%2Fstat")]
    [InlineData("/test\\?#\u0001", "%2Ftest%5C%3F%23%01")]
    [InlineData("/literal%2F", "%2Fliteral%252F")]
    public async Task EncodedKeysRoundTripWithoutChangingCommands(string command, string rowKey)
    {
        var table = new CallsTable();
        using var services = CreateServices(table);
        var repository = services.GetRequiredService<ICommandCallsRepository>();

        Assert.True(await repository.TryAcquireAsync(-100, command, Now, TimeSpan.FromMinutes(5)));
        Assert.Equal(rowKey, table.Saved!.RowKey);

        table.Rows.Add(table.Saved);
        var page = await repository.GetPageAsync(-100, 25, null);
        Assert.Equal(command, Assert.Single(page.Calls).Command);
    }

    [Theory]
    [InlineData(60, 59, false)]
    [InlineData(60, 60, true)]
    [InlineData(600, 599, false)]
    [InlineData(600, 600, true)]
    public async Task HonorsConfiguredInterval(int cooldownSeconds, int elapsedSeconds, bool allowed)
    {
        var table = new CallsTable { Existing = Entry(Now.AddSeconds(-elapsedSeconds)) };
        using var services = CreateServices(table);
        Assert.Equal(allowed, await services.GetRequiredService<ICommandCallsRepository>()
            .TryAcquireAsync(-100, "/stat", Now, TimeSpan.FromSeconds(cooldownSeconds)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-100L)]
    public async Task ListFiltersByChatAndReturnsTimestampAndContinuation(long? chatId)
    {
        var table = new CallsTable { ResultToken = "next" };
        table.Rows.Add(Entry(Now));
        using var services = CreateServices(table);
        var page = await services.GetRequiredService<ICommandCallsRepository>().GetPageAsync(chatId, 25, "previous");
        Assert.Equal(chatId is null ? null : TableClient.CreateQueryFilter($"PartitionKey eq {"-100"}"), table.Filter);
        var call = Assert.Single(page.Calls);
        Assert.Equal(-100, call.ChatId);
        Assert.Equal("/stat", call.Command);
        Assert.Equal(Now, call.LastCall);
        Assert.Equal("next", page.ContinuationToken);
        Assert.Equal("previous", table.RequestedToken);
        Assert.Equal(25, table.RequestedPageSize);
    }

    [Fact]
    public async Task MissingCommandCallsTableListsAsEmpty()
    {
        using var services = CreateServices(new CallsTable { MissingTable = true });
        var page = await services.GetRequiredService<ICommandCallsRepository>().GetPageAsync(null, 100, null);
        Assert.Empty(page.Calls);
        Assert.Null(page.ContinuationToken);
    }

    private static TableEntity Entry(DateTimeOffset lastCall) => new("-100", "%2Fstat")
    {
        ["lastCall"] = lastCall,
        ETag = new ETag("version-1")
    };

    private static ServiceProvider CreateServices(CallsTable table) => new ServiceCollection()
        .AddPersistence().AddKeyedSingleton<TableClient>("CommandsCall", table).BuildServiceProvider();

    private sealed class CallsTable : TableClient
    {
        public TableEntity? Existing { get; init; }
        public TableEntity? Saved { get; private set; }
        public ETag UpdateETag { get; private set; }
        public int? WriteFailure { get; init; }
        public bool MissingTable { get; set; }
        public int Created { get; private set; }
        public List<TableEntity> Rows { get; } = [];
        public string? ResultToken { get; init; }
        public string? Filter { get; private set; }
        public string? RequestedToken { get; private set; }
        public int? RequestedPageSize { get; private set; }

        public override AsyncPageable<T> QueryAsync<T>(string? filter = null, int? maxPerPage = null,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            Filter = filter;
            Assert.Equal(new[] { "PartitionKey", "RowKey", "lastCall" }, select);
            if (MissingTable)
                throw new RequestFailedException(404, "Table not found");
            return new TestPageable<T>(Page<T>.FromValues(Rows.Cast<T>().ToArray(), ResultToken, null!),
                (token, size) => { RequestedToken = token; RequestedPageSize = size; });
        }

        public override Task<Response<T>> GetEntityAsync<T>(string partitionKey, string rowKey,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            AssertValidKey(rowKey);
            if (Existing is null || Existing.PartitionKey != partitionKey || Existing.RowKey != rowKey)
                throw new RequestFailedException(404, "Not found");
            return Task.FromResult(Response.FromValue((T)(object)Existing, null!));
        }

        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            AssertValidKey(entity.RowKey);
            if (MissingTable)
                throw new RequestFailedException(404, "Table not found");
            if (WriteFailure is { } status)
                throw new RequestFailedException(status, "Concurrent write");
            Saved = (TableEntity)(object)entity;
            return Task.FromResult<Response>(null!);
        }

        public override Task<Response> UpdateEntityAsync<T>(T entity, ETag ifMatch,
            TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
        {
            AssertValidKey(entity.RowKey);
            UpdateETag = ifMatch;
            Assert.Equal(TableUpdateMode.Replace, mode);
            if (WriteFailure is { } status)
                throw new RequestFailedException(status, "Concurrent write");
            Saved = (TableEntity)(object)entity;
            return Task.FromResult<Response>(null!);
        }

        public override Task<Response<TableItem>> CreateIfNotExistsAsync(CancellationToken cancellationToken = default)
        {
            Created++;
            MissingTable = false;
            return Task.FromResult<Response<TableItem>>(null!);
        }

        private static void AssertValidKey(string key) =>
            Assert.DoesNotContain(key, character => character is '/' or '\\' or '#' or '?' ||
                character <= '\u001f' || character is >= '\u007f' and <= '\u009f');
    }
}

internal sealed class TestPageable<T>(Page<T> page, Action<string?, int?> onPage) : AsyncPageable<T> where T : notnull
{
    public override IAsyncEnumerable<Page<T>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
    {
        onPage(continuationToken, pageSizeHint);
        return Enumerate();
    }

    private async IAsyncEnumerable<Page<T>> Enumerate()
    {
        await Task.CompletedTask;
        yield return page;
    }
}