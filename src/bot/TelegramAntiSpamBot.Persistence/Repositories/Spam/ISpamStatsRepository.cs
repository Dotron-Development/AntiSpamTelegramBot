namespace TelegramAntiSpamBot.Persistence.Repositories;

public interface ISpamStatsRepository
{
    Task UpdateChannelSpamStats(long chatId, int removingTime);
    Task<SpamStatsResult<int>> GetDailyStats(long chatId);
    Task<SpamStatsResult<int>> GetMonthlyStats(long chatId);
    Task<SpamStatsResult<SpamStatsTimings>> GetTimingsStats(long chatId);
}
