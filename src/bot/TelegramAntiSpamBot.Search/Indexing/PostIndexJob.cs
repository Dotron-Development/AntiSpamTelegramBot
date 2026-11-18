namespace TelegramAntiSpamBot.Search;

public sealed record PostIndexJob(long ChatId, long MessageId, long Revision, string Text, int TitleOffset = 0, int TitleLength = 0)
{
    public const string QueueName = "search-index";
    public const string QueueConnection = "SearchQueueStorage";
}
