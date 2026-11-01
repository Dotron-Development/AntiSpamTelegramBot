
namespace TelegramAntiSpamBot.OpenAI
{
    public interface ISpamDetectionService
    {
        Task<SpamRequestResult> IsSpamAsync(MessagePayload userMessage, int userMessagesCount, bool explainDecision = false);
        Task<ImageAnalysisResult> ExtractImageText(string imageUrl);
    }
}