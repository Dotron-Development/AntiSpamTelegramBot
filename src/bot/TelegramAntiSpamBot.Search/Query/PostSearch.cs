namespace TelegramAntiSpamBot.Search;

public sealed class PostSearch(IPostSnapshotRepository snapshots, ISearchEmbeddings embeddings)
{
    private const int MaxSnapshotBytes = 160 * 1024 * 1024;
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim loadGate = new(1);
    private (long ChannelId, string? ETag, SnapshotSearchIndex? Index, long CheckedAt)? cache;

    public async Task<IReadOnlyList<PostSearchResult>?> FindAsync(long channelId, string text, int limit = 5,
        CancellationToken cancellationToken = default)
    {
        text = text.Trim();
        if (text.Length is 0 or > 4096) throw new ArgumentException("Invalid search query.", nameof(text));
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var index = await ReadIndexAsync(channelId, cancellationToken);
        if (index is null) return null;
        var output = await embeddings.GenerateAsync([text], cancellationToken);
        if (output.Count != 1 || !VectorFormat.IsValid(output[0].Span))
            throw new InvalidDataException("Invalid query embedding.");
        return index.Find(output[0].Span, text, limit, cancellationToken);
    }

    private async Task<SnapshotSearchIndex?> ReadIndexAsync(long channelId, CancellationToken ct)
    {
        await loadGate.WaitAsync(ct);
        try
        {
            var current = cache is { } entry && entry.ChannelId == channelId ? cache : null;
            if (current is { } recent && Stopwatch.GetElapsedTime(recent.CheckedAt) < RecheckInterval)
                return recent.Index;

            var name = channelId.ToString(CultureInfo.InvariantCulture);
            var response = await snapshots.OpenIfChangedAsync(name, current?.ETag, ct);
            if (response is null)
            {
                cache = (channelId, null, null, Stopwatch.GetTimestamp());
                return null;
            }
            if (response.Content is null && current is { } unchanged && unchanged.ETag == response.ETag)
            {
                cache = (channelId, unchanged.ETag, unchanged.Index, Stopwatch.GetTimestamp());
                return unchanged.Index;
            }

            await using var data = response.Content ?? throw new InvalidDataException("Snapshot content missing.");
            if (response.Length is <= 0 or > MaxSnapshotBytes)
                throw new InvalidDataException("Search snapshot exceeds the supported size or is empty.");
            var updated = await SnapshotSearchIndex.ReadAsync(data, name, response.Length, ct);
            cache = (channelId, response.ETag, updated, Stopwatch.GetTimestamp());
            return updated;
        }
        finally
        {
            loadGate.Release();
        }
    }
}
