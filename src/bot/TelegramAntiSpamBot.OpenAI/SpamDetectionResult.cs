namespace TelegramAntiSpamBot.OpenAI
{
    internal sealed record SpamDetectionResult(int? Probability, string? Explanation);
}
