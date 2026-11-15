namespace TelegramAntiSpamBot.Persistence.Repositories
{
    internal class ChatsRepository(
        [FromKeyedServices("Chats")] TableClient chatsTable) : IChatsRepository
    {
        public async Task SaveChatAsync(ReviewChat chat)
        {
            var entry = new ChatEntry
            {
                PartitionKey = ChatEntry.CatalogPartitionKey,
                RowKey = chat.ChatId.ToString(CultureInfo.InvariantCulture),
                Name = chat.Name,
                LastSeenUtc = chat.LastSeenUtc
            };
            try
            {
                await chatsTable.UpsertEntityAsync(entry, TableUpdateMode.Replace);
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                await chatsTable.CreateIfNotExistsAsync();
                await chatsTable.UpsertEntityAsync(entry, TableUpdateMode.Replace);
            }
        }

        public async Task<ReviewChatPage> GetReviewChatsAsync(int pageSize, string? continuationToken)
        {
            try
            {
                var query = chatsTable.QueryAsync<ChatEntry>(entry => entry.PartitionKey == ChatEntry.CatalogPartitionKey);
                await foreach (var page in query.AsPages(continuationToken, pageSize))
                {
                    return new ReviewChatPage(page.Values.Select(entry => new ReviewChat(
                        long.Parse(entry.RowKey, CultureInfo.InvariantCulture),
                        entry.Name, entry.LastSeenUtc)).ToArray(), page.ContinuationToken);
                }
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                return new ReviewChatPage([], null);
            }
            return new ReviewChatPage([], null);
        }
    }
}
