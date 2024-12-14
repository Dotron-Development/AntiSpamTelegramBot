namespace TelegramAntiSpamBot.Persistence.Entities
{
    public class SpamHistoryEntry : AzureTableEntry
    {
        public required long UserId { get; init; }
        public required string Message { get; init; }
        public required int Probability { get; init; }

        public SpamHistoryEntry()
        {

        }

        [SetsRequiredMembers]
        public SpamHistoryEntry(long chatId, long userId, string message, int probability)
        {
            UserId = userId;
            Message = message;
            Probability = probability;
            PartitionKey = chatId.ToString();
            RowKey = Guid.NewGuid().ToString();
        }
    }
}
