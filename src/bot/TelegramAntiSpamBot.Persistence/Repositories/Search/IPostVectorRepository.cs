namespace TelegramAntiSpamBot.Persistence.Repositories;

public interface IPostVectorRepository
{
    Task SaveAsync(long chatId, IndexedPost post, CancellationToken cancellationToken = default);
    Task<bool> ShouldIndexAsync(long chatId, long messageId, long revision, string fingerprint, CancellationToken cancellationToken = default);
    IAsyncEnumerable<IndexedPost> ListAsync(long chatId, CancellationToken cancellationToken = default);
}
