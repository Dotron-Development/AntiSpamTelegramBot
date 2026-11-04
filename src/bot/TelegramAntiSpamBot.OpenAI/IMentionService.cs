namespace TelegramAntiSpamBot.OpenAI;

public interface IMentionService
{
    Task<string> GenerateAsync(MentionRequest request, CancellationToken cancellationToken = default);
}
