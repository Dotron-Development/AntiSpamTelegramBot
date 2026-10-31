using System.Globalization;
using System.Runtime.CompilerServices;

namespace TelegramAntiSpamBot.Persistence.Repositories;

internal sealed class MessageHistoryRepository(
    [FromKeyedServices("MessageHistory")] TableClient historyTable) : IMessageHistoryRepository
{
    public async Task<HistoryMessagePage> GetPageAsync(long? chatId, int pageSize, string? continuationToken,
        CancellationToken cancellationToken = default)
    {
        var partition = chatId?.ToString(CultureInfo.InvariantCulture);
        var filter = partition is null ? null : TableClient.CreateQueryFilter($"PartitionKey eq {partition}");
        try
        {
            await foreach (var page in historyTable.QueryAsync<TableEntity>(filter: filter,
                cancellationToken: cancellationToken).AsPages(continuationToken, pageSize))
            {
                return new HistoryMessagePage(page.Values.Select(entry => new HistoryMessage(
                    long.Parse(entry.PartitionKey, CultureInfo.InvariantCulture),
                    int.Parse(entry.RowKey.AsSpan(20), CultureInfo.InvariantCulture),
                    entry.GetInt64("DateTimeTicks")!.Value,
                    JsonSerializer.Deserialize<HistoryMessageContent>(entry.GetString("Content"))!))
                    .ToArray(), page.ContinuationToken);
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
        }
        return new HistoryMessagePage([], null);
    }

    public async Task SaveAsync(HistoryMessage message, CancellationToken cancellationToken = default)
    {
        var entry = new TableEntity(
            message.ChatId.ToString(CultureInfo.InvariantCulture),
            message.DateTimeTicks.ToString("D19", CultureInfo.InvariantCulture)
                + "-" + message.MessageId.ToString("D10", CultureInfo.InvariantCulture))
        {
            ["DateTimeTicks"] = message.DateTimeTicks,
            ["Content"] = JsonSerializer.Serialize(message.Content)
        };

        try
        {
            await historyTable.UpsertEntityAsync(entry, TableUpdateMode.Replace, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            await historyTable.CreateIfNotExistsAsync(cancellationToken);
            await historyTable.UpsertEntityAsync(entry, TableUpdateMode.Replace, cancellationToken);
        }
    }

    public async IAsyncEnumerable<HistoryMessage> GetAsync(long chatId, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (from >= to)
            throw new ArgumentException("The start of the history interval must precede its end.", nameof(from));

        var partition = chatId.ToString(CultureInfo.InvariantCulture);
        var start = from.UtcDateTime.Ticks.ToString("D19", CultureInfo.InvariantCulture) + "-";
        var end = to.UtcDateTime.Ticks.ToString("D19", CultureInfo.InvariantCulture) + "-";
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and RowKey ge {start} and RowKey lt {end}");

        await foreach (var entry in historyTable.QueryAsync<TableEntity>(filter: filter, cancellationToken: cancellationToken))
        {
            yield return new HistoryMessage(chatId,
                int.Parse(entry.RowKey.AsSpan(20), CultureInfo.InvariantCulture),
                entry.GetInt64("DateTimeTicks")!.Value,
                JsonSerializer.Deserialize<HistoryMessageContent>(entry.GetString("Content"))!);
        }
    }

    public async Task<int> TrimAsync(CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        var retained = new Queue<TableEntity>(1001);
        string? partition = null;
        var batch = new List<TableTransactionAction>(100);

        async Task FlushAsync()
        {
            if (batch.Count == 0)
                return;
            await historyTable.SubmitTransactionAsync(batch, cancellationToken);
            deleted += batch.Count;
            batch.Clear();
        }

        try
        {
            await foreach (var entry in historyTable.QueryAsync<TableEntity>(
                select: ["PartitionKey", "RowKey"], cancellationToken: cancellationToken))
            {
                if (partition != entry.PartitionKey)
                {
                    await FlushAsync();
                    partition = entry.PartitionKey;
                    retained.Clear();
                }

                retained.Enqueue(entry);
                if (retained.Count <= 1000)
                    continue;

                batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, retained.Dequeue(), ETag.All));
                if (batch.Count == 100)
                    await FlushAsync();
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404 && partition is null)
        {
            return 0;
        }

        await FlushAsync();
        return deleted;
    }
}