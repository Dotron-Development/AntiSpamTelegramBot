namespace TelegramAntiSpamBot.Persistence.Entities
{
    public class SpamStatsTimings
    {
        public int? MessageCount { get; set; }
        public int? AvgDelayMs { get; set; }
        public int? MaxDelayMs { get; set; }
        public int? MinDelayMs { get; set; }
    }
}
