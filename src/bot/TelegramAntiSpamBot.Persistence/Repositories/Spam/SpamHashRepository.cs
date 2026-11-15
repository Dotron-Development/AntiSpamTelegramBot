namespace TelegramAntiSpamBot.Persistence.Repositories
{
    internal class SpamHashRepository(
        [FromKeyedServices("SpamHash")] TableClient spamHashTable) : ISpamHashRepository
    {
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
    }
}
