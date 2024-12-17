using TelegramAntiSpamBot.Persistence.Entities;

namespace TelegramAntiSpamBot.Persistence
{
    public class AntiSpamBotRepository(
        [FromKeyedServices("SpamHistory")] TableClient spamHistoryTable,
        [FromKeyedServices("MessageCount")] TableClient messageCountTable)
    {
        public async Task SaveMessageAsync(SpamHistoryEntry entry)
        {
            await spamHistoryTable.AddEntityAsync(entry);
        }

        public async Task IncreaseMessageCount(long chatId, long userId)
        {
            var count = await GetUserMessageCount(chatId, userId);
            if (count > 0) 
            {
                await messageCountTable.UpdateEntityAsync(new MessageCountEntry(chatId, userId, count + 1), ETag.All);
            }
            else
            {
                await messageCountTable.UpsertEntityAsync(new MessageCountEntry(chatId, userId, 1));
            }
        }

        public async Task<int> GetUserMessageCount(long chatId, long userId)
        {
            var entryResponse = await messageCountTable.GetEntityIfExistsAsync<MessageCountEntry>(chatId.ToString(), userId.ToString());
            if (entryResponse.HasValue)
            {
                return entryResponse.Value?.Count ?? 0;
            }
            return 0;
        }
    }
}