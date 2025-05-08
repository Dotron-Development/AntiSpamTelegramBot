namespace TelegramAntiSpamBot.Persistence;

public interface IAntiSpamBotRepository
{
    Task SaveMessageAsync(long chatId,
        long userId,
        string messageContent,
        int probability, 
        string? explanation = null);

    Task IncreaseMessageCount(long chatId, long userId);
    Task<int> GetUserMessageCount(long chatId, long userId);
    Task<ShortcutCheckResult> IsSpam(string hex);
    Task SaveMessageHash(string hash, bool isSpam);
    Task UpdateChannelSpamStats(long chatId, int removingTime);
    Task<SpamStatsResult<int>> GetDailyStats(long chatId);
    Task<SpamStatsResult<int>> GetMonthlyStats(long chatId);
    Task<SpamStatsResult<SpamStatsTimings>> GetTimingsStats(long chatId);
}