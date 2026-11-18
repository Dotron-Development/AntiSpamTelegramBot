namespace TelegramAntiSpamBot.IntegrationTests;

internal sealed class PostVectorRepositoryStub : IPostVectorRepository
{
    public IndexedPost? Post { get; private set; }
    public int Saves { get; private set; }
    public Task<bool> ShouldIndexAsync(long chatId, long messageId, long revision, string fingerprint, CancellationToken cancellationToken = default) =>
        Task.FromResult(Post is null || Post.Revision < revision && Post.Fingerprint != fingerprint);
    public Task SaveAsync(long chatId, IndexedPost post, CancellationToken cancellationToken = default)
    {
        Post = post;
        Saves++;
        return Task.CompletedTask;
    }
    public IAsyncEnumerable<IndexedPost> ListAsync(long chatId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
