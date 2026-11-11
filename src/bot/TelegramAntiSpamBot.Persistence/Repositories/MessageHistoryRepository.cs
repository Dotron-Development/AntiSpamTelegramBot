using Microsoft.Extensions.Logging;

namespace TelegramAntiSpamBot.Persistence.Repositories;

internal sealed class MessageHistoryRepository(
    [FromKeyedServices("MessageHistory")] TableClient historyTable,
    ILogger<MessageHistoryRepository> logger) : IMessageHistoryRepository
{
    private const int MaxContentCharacters = 32 * 1024;

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
                    int.Parse(entry.RowKey.AsSpan(20, 10), CultureInfo.InvariantCulture),
                    entry.GetInt64("DateTimeTicks")!.Value,
                    DeserializeContent(entry.GetString("Content")), ReadUpdateType(entry), entry.GetInt32("UpdateId")))
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
        var (content, truncated) = SerializeContent(message);
        var entry = new TableEntity(
            message.ChatId.ToString(CultureInfo.InvariantCulture),
            GetRowKey(message.DateTimeTicks, message.MessageId))
        {
            ["DateTimeTicks"] = message.DateTimeTicks,
            ["UpdateType"] = message.UpdateType.ToString(),
            ["Content"] = content
        };

        if (truncated)
            entry["ContentTruncated"] = true;

        if (message.UpdateId is { } id)
            entry["UpdateId"] = id;

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

    private (string Content, bool Truncated) SerializeContent(HistoryMessage message)
    {
        var content = JsonSerializer.Serialize(message.Content);
        if (content.Length <= MaxContentCharacters)
            return (content, false);

        var originalLength = content.Length;
        var low = 0;
        var high = MaxContentCharacters;
        while (low < high)
        {
            var limit = low + (high - low + 1) / 2;
            if (JsonSerializer.Serialize(TruncateContent(message.Content, limit)).Length <= MaxContentCharacters)
                low = limit;
            else
                high = limit - 1;
        }

        content = JsonSerializer.Serialize(TruncateContent(message.Content, low));
        logger.LogWarning("Truncated message history for chat {ChatId}, message {MessageId}: JSON characters {OriginalLength} -> {StoredLength}; user text {UserTextLength}, image text {ImageTextLength}, image description {ImageDescriptionLength}, reply text {ReplyTextLength}",
            message.ChatId, message.MessageId, originalLength, content.Length,
            message.Content.Payload.UserMessage?.Length ?? 0, message.Content.Payload.ImageText?.Length ?? 0,
            message.Content.Payload.ImageDescription?.Length ?? 0, message.Content.ReplyTo?.Text.Length ?? 0);
        return (content, true);
    }

    private static HistoryMessageContent TruncateContent(HistoryMessageContent content, int limit) => content with
    {
        ReplyTo = content.ReplyTo is { } reply ? reply with
        {
            Text = TruncateText(reply.Text, limit)!
        } : null,
        Payload = content.Payload with
        {
            UserMessage = TruncateText(content.Payload.UserMessage, limit),
            ImageText = TruncateText(content.Payload.ImageText, limit),
            ImageDescription = TruncateText(content.Payload.ImageDescription, limit)
        }
    };

    private static string? TruncateText(string? text, int limit)
    {
        if (text is null || text.Length <= limit)
            return text;
        if (limit > 0 && char.IsHighSurrogate(text[limit - 1]) && char.IsLowSurrogate(text[limit]))
            limit--;
        return text[..limit];
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
                int.Parse(entry.RowKey.AsSpan(20, 10), CultureInfo.InvariantCulture),
                entry.GetInt64("DateTimeTicks")!.Value,
                DeserializeContent(entry.GetString("Content")), ReadUpdateType(entry), entry.GetInt32("UpdateId"));
        }
    }

    private static string GetRowKey(long dateTimeTicks, int messageId) =>
        dateTimeTicks.ToString("D19", CultureInfo.InvariantCulture)
            + "-" + messageId.ToString("D10", CultureInfo.InvariantCulture);

    private static HistoryUpdateType ReadUpdateType(TableEntity entry) =>
        Enum.TryParse<HistoryUpdateType>(entry.GetString("UpdateType"), out var updateType)
            ? updateType : HistoryUpdateType.Message;

    private static HistoryMessageContent DeserializeContent(string json)
    {
        using var document = JsonDocument.Parse(json);
        var content = JsonSerializer.Deserialize<HistoryMessageContent>(json)!;
        if (content.Payload is not null)
            return content;

        var text = document.RootElement.TryGetProperty("Text", out var legacyText)
            && legacyText.ValueKind == JsonValueKind.String ? legacyText.GetString() : null;
        return content with { Payload = new MessagePayload(UserMessage: text) };
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