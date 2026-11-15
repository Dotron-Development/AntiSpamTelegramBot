namespace TelegramAntiSpamBot.OpenAI;

public interface IMentionService
{
    Task<MentionResponse> GenerateAsync(MentionRequest request, CancellationToken cancellationToken = default);
}
