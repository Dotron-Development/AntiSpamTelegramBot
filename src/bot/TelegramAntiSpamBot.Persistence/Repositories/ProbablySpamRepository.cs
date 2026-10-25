namespace TelegramAntiSpamBot.Persistence.Repositories
{
    internal class ProbablySpamRepository(
        [FromKeyedServices("ProbablySpam")] TableClient probablySpamTable) : IProbablySpamRepository
    {
        public async Task SaveReviewMessageAsync(ReviewMessage message)
        {
            var entry = new ProbablySpamEntry
            {
                PartitionKey = message.ChatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                RowKey = message.MessageId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                UserId = message.UserId,
                UserName = message.UserName,
                Message = message.Message,
                ImageText = message.ImageText,
                ContentHash = message.ContentHash,
                MessageDateTicks = message.MessageDateTicks
            };
            await probablySpamTable.UpsertEntityAsync(entry, TableUpdateMode.Replace);
        }

        public async Task<ReviewMessagePage> GetReviewMessagesAsync(long? chatId, int pageSize, string? continuationToken)
        {
            var filter = chatId is { } id
                ? TableClient.CreateQueryFilter($"PartitionKey eq {id.ToString(System.Globalization.CultureInfo.InvariantCulture)}")
                : null;
            var query = probablySpamTable.QueryAsync<ProbablySpamEntry>(filter: filter);
            await foreach (var page in query.AsPages(continuationToken, pageSize))
            {
                return new ReviewMessagePage(page.Values.Select(ToReviewMessage).ToArray(), page.ContinuationToken);
            }
            return new ReviewMessagePage([], null);
        }

        public async Task<ReviewMessage?> GetReviewMessageAsync(long chatId, int messageId)
        {
            var response = await probablySpamTable.GetEntityIfExistsAsync<ProbablySpamEntry>(
                chatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                messageId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return response.HasValue ? ToReviewMessage(response.Value!) : null;
        }

        public async Task DeleteReviewMessageAsync(long chatId, int messageId)
        {
            await probablySpamTable.DeleteEntityAsync(
                chatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                messageId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static ReviewMessage ToReviewMessage(ProbablySpamEntry entry) => new(
            long.Parse(entry.PartitionKey, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(entry.RowKey, System.Globalization.CultureInfo.InvariantCulture), entry.UserId,
            entry.UserName, entry.Message, entry.ImageText, entry.ContentHash, entry.MessageDateTicks);
    }
}
