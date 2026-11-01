namespace TelegramAntiSpamBot.OpenAI
{
    [method: SetsRequiredMembers]
    internal class SpamDetectionRequest(MessagePayload message, int userMessageCount)
    {
        public required MessagePayload Message { get; init; } = message;
        public required int UserMessageCount { get; init; } = userMessageCount;
    }
}
