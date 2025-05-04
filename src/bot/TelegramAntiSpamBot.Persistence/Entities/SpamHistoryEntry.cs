namespace TelegramAntiSpamBot.Persistence.Entities
{
    public class SpamHistoryEntry : AzureTableEntry
    {
        public required long UserId { get; init; }
        public required string Message { get; init; }
        public required int Probability { get; init; }
        public string? Explanation { get; init; }

        // Reason: No parameterless constructor defined error when using Azure Table Storage
        public SpamHistoryEntry()
        {

        }

        [SetsRequiredMembers]
        public SpamHistoryEntry(long chatId, long userId, string message, int probability, string? explanation = null)
        {
            UserId = userId;
            Message = message;
            Probability = probability;
            PartitionKey = chatId.ToString();
            RowKey = Guid.NewGuid().ToString();
            Explanation = explanation;
        }
    }
}
