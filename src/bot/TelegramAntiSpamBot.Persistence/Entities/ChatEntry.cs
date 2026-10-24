namespace TelegramAntiSpamBot.Persistence.Entities;

internal class ChatEntry : AzureTableEntry
{
    public const string CatalogPartitionKey = "chats";
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset LastSeenUtc { get; set; }
}