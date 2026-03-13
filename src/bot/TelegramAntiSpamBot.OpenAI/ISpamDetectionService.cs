
namespace TelegramAntiSpamBot.OpenAI
{
    public interface ISpamDetectionService
    {
        Task<SpamRequestResult> IsSpamAsync(string userMessage, int userMessagesCount, bool explainDecision = false);
        Task<string> ExtractImageText(string imageUrl);
    }
}