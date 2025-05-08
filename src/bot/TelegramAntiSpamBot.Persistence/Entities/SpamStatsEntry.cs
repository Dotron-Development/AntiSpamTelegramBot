namespace TelegramAntiSpamBot.Persistence.Entities
{
    internal class SpamStatsEntry : AzureTableEntry
    {
        public required string Value { get; init; }

        // Reason: No parameterless constructor defined error when using Azure Table Storage
        public SpamStatsEntry()
        {

        }

        [SetsRequiredMembers]
        public SpamStatsEntry(long chatId, string rowKey, string value)
        {
            PartitionKey = chatId.ToString();
            RowKey = rowKey;
            Value = value;
        }
    }
}
