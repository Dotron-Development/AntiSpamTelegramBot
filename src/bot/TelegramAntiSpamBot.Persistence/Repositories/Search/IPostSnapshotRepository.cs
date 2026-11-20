namespace TelegramAntiSpamBot.Persistence.Repositories;

public interface IPostSnapshotRepository
{
    IAsyncEnumerable<SnapshotBlob> ListAsync(CancellationToken cancellationToken);
    Task<SnapshotBlob?> FindAsync(string name, CancellationToken cancellationToken);
    Task PublishAsync(string name, DateTimeOffset requestedAt, Func<Stream, CancellationToken, Task<bool>> write,
        CancellationToken cancellationToken);
}
