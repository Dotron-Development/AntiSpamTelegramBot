namespace TelegramAntiSpamBot.Search;

public sealed record SnapshotResetJob(long ChatId, DateTimeOffset RequestedAt)
{
    public const string QueueName = "reset-snapshot";
}
