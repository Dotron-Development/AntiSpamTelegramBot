namespace TelegramAntiSpamBot.Persistence.Entities
{
    internal class SpamHashEntry : AzureTableEntry
    {
        public required bool IsSpam { get; init; }

        // Reason: No parameterless constructor defined error when using Azure Table Storage
        public SpamHashEntry()
        {

        }

        [SetsRequiredMembers]
        public SpamHashEntry(string hash, bool isSpam)
        {
            PartitionKey = "1";
            RowKey = hash;
            IsSpam = isSpam;
        }
    }
}
