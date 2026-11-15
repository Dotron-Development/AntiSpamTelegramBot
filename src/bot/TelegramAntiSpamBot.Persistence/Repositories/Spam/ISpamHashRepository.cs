namespace TelegramAntiSpamBot.Persistence.Repositories;

public interface ISpamHashRepository
{
    Task<ShortcutCheckResult> IsSpam(string hex);
    Task SaveMessageHash(string hash, bool isSpam);
}
