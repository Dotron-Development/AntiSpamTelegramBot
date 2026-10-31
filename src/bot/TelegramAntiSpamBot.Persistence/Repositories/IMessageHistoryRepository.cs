namespace TelegramAntiSpamBot.Persistence.Repositories;

public interface IMessageHistoryRepository
{
    Task SaveAsync(HistoryMessage message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<HistoryMessage> GetAsync(long chatId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
    Task<int> TrimAsync(CancellationToken cancellationToken = default);
    Task<DTO.HistoryMessagePage> GetPageAsync(long? chatId, int pageSize, string? continuationToken,
        CancellationToken cancellationToken = default);
}