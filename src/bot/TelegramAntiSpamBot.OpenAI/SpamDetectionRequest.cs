namespace TelegramAntiSpamBot.OpenAI
{
    [method: SetsRequiredMembers]
    internal class SpamDetectionRequest(MessagePayload message, int userMessageCount, bool isReported = false)
    {
        public required MessagePayload Message { get; init; } = message;
        public required int UserMessageCount { get; init; } = userMessageCount;
        public bool IsReported { get; init; } = isReported;
    }
}
