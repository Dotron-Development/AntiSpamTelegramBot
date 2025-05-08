namespace TelegramAntiSpamBot.Persistence.Entities
{
    internal class AzureTableEntry : ITableEntity
    {
        public required string PartitionKey { get; set; }
        public required string RowKey { get; set; }

        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
