
namespace TelegramAntiSpamBot.Persistence.Repositories;

internal sealed class CommandCallsRepository(
    [FromKeyedServices("CommandsCall")] TableClient table) : ICommandCallsRepository
{
    public async Task<CommandCallPage> GetPageAsync(long? chatId, int pageSize, string? continuationToken,
        CancellationToken cancellationToken = default)
    {
        var partition = chatId?.ToString(CultureInfo.InvariantCulture);
        var filter = partition is null ? null : TableClient.CreateQueryFilter($"PartitionKey eq {partition}");
        try
        {
            await foreach (var page in table.QueryAsync<TableEntity>(filter: filter,
                select: ["PartitionKey", "RowKey", "lastCall"], cancellationToken: cancellationToken)
                .AsPages(continuationToken, pageSize))
            {
                return new CommandCallPage(page.Values.Select(entry => new CommandCall(
                    long.Parse(entry.PartitionKey, CultureInfo.InvariantCulture), Uri.UnescapeDataString(entry.RowKey),
                    entry.GetDateTimeOffset("lastCall")!.Value)).ToArray(), page.ContinuationToken);
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
        }
        return new CommandCallPage([], null);
    }

    public async Task<bool> TryAcquireAsync(long chatId, string command, DateTimeOffset now, TimeSpan cooldown,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cooldown, TimeSpan.Zero);
        var partition = chatId.ToString(CultureInfo.InvariantCulture);
        var rowKey = Uri.EscapeDataString(command);
        TableEntity? previous = null;
        try
        {
            previous = (await table.GetEntityAsync<TableEntity>(partition, rowKey,
                cancellationToken: cancellationToken)).Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
        }

        if (previous?.GetDateTimeOffset("lastCall") is { } lastCall && now - lastCall < cooldown)
            return false;

        var entry = new TableEntity(partition, rowKey) { ["lastCall"] = now.ToUniversalTime() };
        try
        {
            if (previous is not null)
            {
                await table.UpdateEntityAsync(entry, previous.ETag, TableUpdateMode.Replace, cancellationToken);
            }
            else
            {
                try
                {
                    await table.AddEntityAsync(entry, cancellationToken);
                }
                catch (RequestFailedException exception) when (exception.Status == 404)
                {
                    await table.CreateIfNotExistsAsync(cancellationToken);
                    await table.AddEntityAsync(entry, cancellationToken);
                }
            }
            return true;
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            return false;
        }
    }
}