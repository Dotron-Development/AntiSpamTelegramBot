namespace TelegramAntiSpamBot.Persistence.Entities
{
    public class MessageCountEntry : AzureTableEntry
    {
        // Reason: No parameterless constructor defined error when using Azure Table Storage
        public MessageCountEntry()
        {

        }

        [SetsRequiredMembers]
        public MessageCountEntry(long chatId, long userId, int count)
        {
            Count = count;
            PartitionKey = chatId.ToString();
            RowKey = userId.ToString();
        }

        public required int Count { get; init; }
    }
}
