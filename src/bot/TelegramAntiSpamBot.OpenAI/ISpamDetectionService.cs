
namespace TelegramAntiSpamBot.OpenAI
{
    public interface ISpamDetectionService
    {
        Task<SpamRequestResult> IsSpam(string userMessage, int userMessagesCount, string? imageUrl = null, bool explainDecision = false);
    }
}