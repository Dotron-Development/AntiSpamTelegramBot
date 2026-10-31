namespace TelegramAntiSpamBot.Persistence.Repositories;

public interface ICommandCallsRepository
{
    Task<bool> TryAcquireAsync(long chatId, string command, DateTimeOffset now, TimeSpan cooldown,
        CancellationToken cancellationToken = default);
    Task<DTO.CommandCallPage> GetPageAsync(long? chatId, int pageSize, string? continuationToken,
        CancellationToken cancellationToken = default);
}