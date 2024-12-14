namespace TelegramAntiSpamBot.OpenAI
{
    [method: SetsRequiredMembers]
    internal class SpamDetectionRequest(string message, int userMessageCount)
    {
        public required string Message { get; init; } = message;
        public required int UserMessageCount { get; init; } = userMessageCount;
    }
}
