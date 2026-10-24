namespace TelegramAntiSpamBot.Persistence
{
    internal class AntiSpamBotRepository(
        [FromKeyedServices("Chats")] TableClient chatsTable,
        [FromKeyedServices("ProbablySpam")] TableClient probablySpamTable,
        [FromKeyedServices("SpamHash")] TableClient spamHashTable,
        [FromKeyedServices("MessageCount")] TableClient messageCountTable,
        [FromKeyedServices("SpamStats")] TableClient spamStatsTable) : IAntiSpamBotRepository
    {
        public async Task SaveChatAsync(ReviewChat chat)
        {
            var entry = new ChatEntry
            {
                PartitionKey = ChatEntry.CatalogPartitionKey,
                RowKey = chat.ChatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = chat.Name,
                LastSeenUtc = chat.LastSeenUtc
            };
            try
            {
                await chatsTable.UpsertEntityAsync(entry, TableUpdateMode.Replace);
            }
            catch (Azure.RequestFailedException exception) when (exception.Status == 404)
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
                        long.Parse(entry.RowKey, System.Globalization.CultureInfo.InvariantCulture),
                        entry.Name, entry.LastSeenUtc)).ToArray(), page.ContinuationToken);
                }
            }
            catch (Azure.RequestFailedException exception) when (exception.Status == 404)
            {
                return new ReviewChatPage([], null);
            }
            return new ReviewChatPage([], null);
        }

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

        public async Task<ReviewMessagePage> GetReviewMessagesAsync(long chatId, int pageSize, string? continuationToken)
        {
            var partitionKey = chatId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var query = probablySpamTable.QueryAsync<ProbablySpamEntry>(entry => entry.PartitionKey == partitionKey);
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
            var result = await spamHashTable.GetEntityIfExistsAsync<SpamHashEntry>("1", hex);
            return result.HasValue
                ? result.Value!.IsSpam ? ShortcutCheckResult.Spam : ShortcutCheckResult.NotSpam
                : ShortcutCheckResult.NotFound;
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