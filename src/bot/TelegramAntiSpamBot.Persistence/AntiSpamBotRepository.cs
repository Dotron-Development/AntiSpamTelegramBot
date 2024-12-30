using TelegramAntiSpamBot.Persistence.Entities;

namespace TelegramAntiSpamBot.Persistence
{
    public class AntiSpamBotRepository(
        [FromKeyedServices("SpamHistory")] TableClient spamHistoryTable,
        [FromKeyedServices("SpamHash")] TableClient spamHashTable,
        [FromKeyedServices("MessageCount")] TableClient messageCountTable)
    {
        public async Task SaveMessageAsync(SpamHistoryEntry entry)
        {
            await spamHistoryTable.UpsertEntityAsync(entry, TableUpdateMode.Replace);
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

        public async Task<bool> IsSpam(string hex)
        {
            var t1 = spamHashTable.GetEntityIfExistsAsync<TableEntity>("1", hex);
            var t2 = spamHashTable.GetEntityIfExistsAsync<TableEntity>("2", hex);
            var t3 = spamHashTable.GetEntityIfExistsAsync<TableEntity>("3", hex);

            await foreach (var task in Task.WhenEach(t1, t2, t3))
            {
                var result = await task;
                if (result.HasValue)
                {
                    return true;
                }
            }

            return false;
        }

        public async Task SaveMessageHash(string hash)
        {
            var partition = Random.Shared.Next(1, 4).ToString();
            await spamHashTable.UpsertEntityAsync(new TableEntity(partition, hash), TableUpdateMode.Replace);
        }
    }
}