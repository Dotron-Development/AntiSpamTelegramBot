namespace TelegramAntiSpamBot.Persistence.Repositories;

public interface IProbablySpamRepository
{
    Task SaveReviewMessageAsync(ReviewMessage message);
    Task<ReviewMessagePage> GetReviewMessagesAsync(long? chatId, int pageSize, string? continuationToken);
    Task<ReviewMessage?> GetReviewMessageAsync(long chatId, int messageId);
    Task DeleteReviewMessageAsync(long chatId, int messageId);
}
