namespace TelegramAntiSpamBot.Search;

public sealed class PostSnapshotBuilder(IPostVectorRepository vectors, IPostSnapshotRepository snapshots)
{
    internal const int SnapshotSignature = 0x31534354; // TCS1 in little-endian order.

    public IAsyncEnumerable<SnapshotBlob> ListAsync(CancellationToken cancellationToken) => snapshots.ListAsync(cancellationToken);

    public Task<SnapshotBlob?> FindAsync(long channelId, CancellationToken cancellationToken) =>
        snapshots.FindAsync(channelId.ToString(CultureInfo.InvariantCulture), cancellationToken);

    public Task RebuildAsync(SnapshotResetJob job, CancellationToken cancellationToken)
    {
        if (job.ChatId >= 0 || job.RequestedAt == default) throw new ArgumentException("Invalid channel snapshot request.", nameof(job));
        var name = job.ChatId.ToString(CultureInfo.InvariantCulture);
        return snapshots.PublishAsync(name, job.RequestedAt,
            (target, ct) => WriteAsync(name, vectors.ListAsync(job.ChatId, ct), target, ct), cancellationToken);
    }

    private static async Task<bool> WriteAsync(string chat, IAsyncEnumerable<IndexedPost> source, Stream target, CancellationToken ct)
    {
        await using var spool = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        var posts = new List<(long Id, string Title, int Chunks, long Offset)>();
        var buffer = new byte[VectorFormat.Dimensions * sizeof(float)];
        var chunks = 0;
        await foreach (var post in source.WithCancellation(ct))
        {
            posts.Add((post.MessageId, post.Title, post.Vectors.Count, spool.Position));
            foreach (var vector in post.Vectors)
            {
                for (var i = 0; i < vector.Length; i++)
                    BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(i * sizeof(float)), vector.Span[i]);
                await spool.WriteAsync(buffer, ct);
                chunks++;
            }
        }
        if (posts.Count == 0) return false;
        posts.Sort((a, b) => a.Id.CompareTo(b.Id));

        using var writer = new BinaryWriter(target, Encoding.UTF8, leaveOpen: true);
        writer.Write(SnapshotSignature);
        writer.Write(chat);
        writer.Write(VectorFormat.Model);
        writer.Write(VectorFormat.Version);
        writer.Write(VectorFormat.Dimensions);
        writer.Write(posts.Count);
        writer.Write(chunks);
        foreach (var post in posts)
        {
            writer.Write(post.Id);
            writer.Write(post.Title);
            writer.Write(post.Chunks);
        }
        writer.Flush();
        foreach (var post in posts)
        {
            spool.Position = post.Offset;
            for (var i = 0; i < post.Chunks; i++)
            {
                await spool.ReadExactlyAsync(buffer, ct);
                await target.WriteAsync(buffer, ct);
            }
        }
        target.Position = 0;
        var checksum = await SHA256.HashDataAsync(target, ct);
        await target.WriteAsync(checksum, ct);
        return true;
    }
}
