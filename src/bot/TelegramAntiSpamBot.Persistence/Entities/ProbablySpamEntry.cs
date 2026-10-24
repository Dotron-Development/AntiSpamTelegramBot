namespace TelegramAntiSpamBot.Persistence.Entities;

internal class ProbablySpamEntry : AzureTableEntry
{
    public long UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ImageText { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public long MessageDateTicks { get; set; }
}