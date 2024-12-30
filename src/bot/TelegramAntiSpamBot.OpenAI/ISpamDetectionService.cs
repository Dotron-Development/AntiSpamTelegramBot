
namespace TelegramAntiSpamBot.OpenAI
{
    public interface ISpamDetectionService
    {
        Task<SpamRequestResult> IsSpam(string userMessage, int userMessagesCount, bool explainDecision = false);
        Task<string> ExtractImageText(string imageUrl);
    }
}