namespace TelegramAntiSpamBot.Persistence;

public interface IAntiSpamBotRepository
{
    Task SaveChatAsync(ReviewChat chat);
    Task<ReviewChatPage> GetReviewChatsAsync(int pageSize, string? continuationToken);
    Task SaveReviewMessageAsync(ReviewMessage message);
    Task<ReviewMessagePage> GetReviewMessagesAsync(long chatId, int pageSize, string? continuationToken);
    Task<ReviewMessage?> GetReviewMessageAsync(long chatId, int messageId);
    Task DeleteReviewMessageAsync(long chatId, int messageId);

    Task IncreaseMessageCount(long chatId, long userId);
    Task<int> GetUserMessageCount(long chatId, long userId);
    Task<ShortcutCheckResult> IsSpam(string hex);
    Task SaveMessageHash(string hash, bool isSpam);
    Task UpdateChannelSpamStats(long chatId, int removingTime);
    Task<SpamStatsResult<int>> GetDailyStats(long chatId);
    Task<SpamStatsResult<int>> GetMonthlyStats(long chatId);
    Task<SpamStatsResult<SpamStatsTimings>> GetTimingsStats(long chatId);
}