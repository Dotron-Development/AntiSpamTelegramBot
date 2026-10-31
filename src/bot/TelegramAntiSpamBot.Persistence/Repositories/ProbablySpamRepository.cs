
namespace TelegramAntiSpamBot.Persistence.Repositories
{
    internal class ProbablySpamRepository(
        [FromKeyedServices("ProbablySpam")] TableClient probablySpamTable) : IProbablySpamRepository
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public async Task SaveReviewMessageAsync(ReviewMessage message)
        {
            var entry = new ProbablySpamEntry
            {
                PartitionKey = message.ChatId.ToString(CultureInfo.InvariantCulture),
                RowKey = message.MessageId.ToString(CultureInfo.InvariantCulture),
                UserId = message.UserId,
                From = JsonSerializer.Serialize(message.From, JsonOptions),
                ReplyTo = message.ReplyTo is null ? string.Empty : JsonSerializer.Serialize(message.ReplyTo, JsonOptions),
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
                ? TableClient.CreateQueryFilter($"PartitionKey eq {id.ToString(CultureInfo.InvariantCulture)}")
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
                chatId.ToString(CultureInfo.InvariantCulture),
                messageId.ToString(CultureInfo.InvariantCulture));
            return response.HasValue ? ToReviewMessage(response.Value!) : null;
        }

        public async Task DeleteReviewMessageAsync(long chatId, int messageId)
        {
            await probablySpamTable.DeleteEntityAsync(
                chatId.ToString(CultureInfo.InvariantCulture),
                messageId.ToString(CultureInfo.InvariantCulture));
        }

        private static ReviewMessage ToReviewMessage(ProbablySpamEntry entry) => new(
            long.Parse(entry.PartitionKey, CultureInfo.InvariantCulture),
            int.Parse(entry.RowKey, CultureInfo.InvariantCulture), entry.UserId,
            DeserializeFrom(entry), DeserializeReplyTo(entry), entry.Message, entry.ImageText, entry.ContentHash, entry.MessageDateTicks);

        private static ReviewMessageReplyTo? DeserializeReplyTo(ProbablySpamEntry entry)
        {
            var json = entry.ReplyTo;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    if (JsonSerializer.Deserialize<ReviewMessageReplyTo>(json, JsonOptions) is { MessageId: > 0 } replyTo)
                        return replyTo;
                }
                catch (JsonException)
                {
                }
            }

            return null;
        }

        private static ReviewMessageFrom DeserializeFrom(ProbablySpamEntry entry)
        {
            var json = entry.From;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    if (JsonSerializer.Deserialize<ReviewMessageFrom>(json, JsonOptions) is { Name: not null } from)
                        return from;
                }
                catch (JsonException)
                {
                }
            }

            return new ReviewMessageFrom(string.Empty, null);
        }
    }
}
