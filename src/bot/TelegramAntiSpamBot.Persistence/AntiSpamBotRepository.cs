namespace TelegramAntiSpamBot.Persistence
{
    internal class AntiSpamBotRepository(
        [FromKeyedServices("SpamHistory")] TableClient spamHistoryTable,
        [FromKeyedServices("SpamHash")] TableClient spamHashTable,
        [FromKeyedServices("MessageCount")] TableClient messageCountTable,
        [FromKeyedServices("SpamStats")] TableClient spamStatsTable) : IAntiSpamBotRepository
    {
        public async Task SaveMessageAsync(long chatId,
            long userId,
            string messageContent,
            int probability,
            string? explanation = null)
        {
            var entry = new SpamHistoryEntry(chatId, userId, messageContent, probability);
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

        public async Task<ShortcutCheckResult> IsSpam(string hex)
        {
            var t1 = spamHashTable.GetEntityIfExistsAsync<SpamHashEntry>("1", hex);
            var t2 = spamHashTable.GetEntityIfExistsAsync<SpamHashEntry>("2", hex);
            var t3 = spamHashTable.GetEntityIfExistsAsync<SpamHashEntry>("3", hex);

            await foreach (var task in Task.WhenEach(t1, t2, t3))
            {
                var result = await task;
                if (result.HasValue)
                {
                    return result.Value!.IsSpam
                        ? ShortcutCheckResult.Spam
                        : ShortcutCheckResult.NotSpam;
                }
            }

            return ShortcutCheckResult.NotFound;
        }

        public async Task SaveMessageHash(string hash, bool isSpam)
        {
            await spamHashTable.UpsertEntityAsync(new SpamHashEntry(hash, isSpam), TableUpdateMode.Replace);
        }

        public async Task UpdateChannelSpamStats(long chatId, int milliseconds)
        {
            var t1 = IncrementDailyStats(chatId);
            var t2 = IncrementMonthlyStats(chatId);
            var t3 = UpdateTimings(chatId, milliseconds);
            await Task.WhenAll(t1, t2, t3);
        }

        private async Task UpdateTimings(long chatId, int milliseconds)
        {
            var timingsResp = await GetTimingsStats(chatId);
            var timings = timingsResp.Value;

            timings.MessageCount ??= 0;
            timings.AvgDelayMs ??= 0;

            timings.MaxDelayMs = Math.Max(timings.MaxDelayMs ?? 0, milliseconds);
            timings.MinDelayMs = Math.Min(timings.MinDelayMs ?? int.MaxValue, milliseconds);
            timings.AvgDelayMs = ((timings.AvgDelayMs * timings.MessageCount) + milliseconds) / (timings.MessageCount + 1);
            timings.MessageCount += 1;

            var json = JsonSerializer.Serialize(timings);
            await spamStatsTable.UpsertEntityAsync(new SpamStatsEntry(chatId, GetTimingsStatsRowKey(), json), TableUpdateMode.Replace);
        }

        public async Task<SpamStatsResult<int>> GetDailyStats(long chatId)
        {
            var stats = await GetStats(chatId, GetDailyStatsRowKey(DateTime.UtcNow));
            return stats is not null
                ? new SpamStatsResult<int>(int.Parse(stats.Value))
                : new SpamStatsResult<int>(0);
        }

        public async Task<SpamStatsResult<int>> GetMonthlyStats(long chatId)
        {
            var stats = await GetStats(chatId, GetMonthlyStatsRowKey(DateTime.UtcNow));
            return stats is not null
                ? new SpamStatsResult<int>(int.Parse(stats.Value))
                : new SpamStatsResult<int>(0);
        }

        public async Task<SpamStatsResult<SpamStatsTimings>> GetTimingsStats(long chatId)
        {
            var stats = await GetStats(chatId, GetTimingsStatsRowKey());

            return stats is not null
                ? new SpamStatsResult<SpamStatsTimings>(JsonSerializer.Deserialize<SpamStatsTimings>(stats.Value) ?? new SpamStatsTimings())
                : new SpamStatsResult<SpamStatsTimings>(new SpamStatsTimings());
        }

        private async Task<SpamStatsEntry?> GetStats(long chatId, string rowKey)
        {
            var statsResp = await spamStatsTable.GetEntityIfExistsAsync<SpamStatsEntry>(chatId.ToString(), rowKey);
            return statsResp.HasValue ? statsResp.Value : null;
        }

        private Task IncrementMonthlyStats(long chatId)
        {
            var rowKey = GetMonthlyStatsRowKey(DateTime.UtcNow);
            return IncrementStats(chatId, rowKey);
        }

        private Task IncrementDailyStats(long chatId)
        {
            var rowKey = GetDailyStatsRowKey(DateTime.UtcNow);
            return IncrementStats(chatId, rowKey);
        }

        private async Task IncrementStats(long chatId, string rowKey)
        {
            var existingStats = await GetStats(chatId, rowKey);
            if (existingStats is not null)
            {
                var value = int.Parse(existingStats.Value) + 1;
                var stats = new SpamStatsEntry(chatId, rowKey, value.ToString());
                await spamStatsTable.UpdateEntityAsync(stats, ETag.All);
            }
            else
            {
                var stats = new SpamStatsEntry(chatId, rowKey, "1");
                await spamStatsTable.UpsertEntityAsync(stats);
            }
        }

        private static string GetDailyStatsRowKey(DateTime date) => $"DAILY_MESSAGES_DELETED_{date:yyyyMMdd}";
        private static string GetMonthlyStatsRowKey(DateTime date) => $"MONTHLY_MESSAGES_DELETED_{date:yyyyMM}";
        private static string GetTimingsStatsRowKey() => "TIMINGS";
    }
}