namespace TelegramAntiSpamBot.OpenAI
{
    public readonly record struct SpamRequestResult(ResultType ResultType, int? Probability = null);
}
