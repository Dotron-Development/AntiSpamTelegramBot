namespace TelegramAntiSpamBot.Persistence.Repositories;

public interface IChatsRepository
{
    Task SaveChatAsync(ReviewChat chat);
    Task<ReviewChatPage> GetReviewChatsAsync(int pageSize, string? continuationToken);
}
