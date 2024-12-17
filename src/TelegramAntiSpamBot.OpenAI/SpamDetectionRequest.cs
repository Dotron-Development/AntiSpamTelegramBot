namespace TelegramAntiSpamBot.OpenAI
{
    [method: SetsRequiredMembers]
    internal class SpamDetectionRequest(MessageType messageType, string message, int userMessageCount)
    {
        public required string Message { get; init; } = message;
        public required int UserMessageCount { get; init; } = userMessageCount;
        public required MessageType MessageType { get; init; } = messageType;
    }
}
