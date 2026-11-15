namespace TelegramAntiSpamBot.OpenAI;

internal sealed class MentionHistoryTool(
    IMessageHistoryRepository historyRepository,
    MentionConversation conversation,
    CancellationToken cancellationToken,
    ILogger<MentionHistoryTool> logger)
{
    [Description("Retrieve up to 100 messages from the current chat, oldest first. Use from < to <= the current mention timestamp. from is inclusive and to is exclusive; both use ISO 8601 timestamps with a UTC offset. Use a narrow interval. For the next page, pass the returned ContinuationToken unchanged with the same from/to; null means there are no more pages. Errors mean history is unavailable, not empty.")]
    public async Task<MentionHistoryResult> GetMessageHistoryAsync(
        [Description("Inclusive start timestamp in ISO 8601 format with a UTC offset.")] DateTimeOffset from,
        [Description("Exclusive end timestamp in ISO 8601 format, no later than the current mention timestamp.")] DateTimeOffset to,
        [Description("Opaque token returned by the previous call; reuse with the same from and to. Omit or pass null for the first page.")] string? continuationToken = null)
    {
        try
        {
            if (from >= to || to > conversation.Date)
                throw new ArgumentException("Use from < to <= Conversation.Date; history excludes the current mention and later messages.");

            var page = await historyRepository.GetRangePageAsync(conversation.ChatId, from, to,
                continuationToken, cancellationToken);
            return new MentionHistoryResult(page.Messages.Select(message => new MentionHistoryItem(
                message.MessageId,
                new DateTimeOffset(new DateTime(message.DateTimeTicks, DateTimeKind.Utc)),
                message.Content,
                message.UpdateType,
                message.UpdateId)).ToArray(), page.ContinuationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unable to load mention history for chat {ChatId}, from {From} to {To}",
                conversation.ChatId, from, to);
            throw;
        }
    }

}
