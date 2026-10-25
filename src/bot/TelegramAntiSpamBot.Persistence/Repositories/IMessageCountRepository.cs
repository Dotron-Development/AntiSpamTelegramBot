namespace TelegramAntiSpamBot.Persistence.Repositories;

public interface IMessageCountRepository
{
    Task IncreaseMessageCount(long chatId, long userId);
    Task<int> GetUserMessageCount(long chatId, long userId);
}
