namespace TelegramAntiSpamBot.IntegrationTests;

public sealed class PostSnapshotTests
{
    [Fact]
    public async Task PublishesChecksummedSnapshotInNumericPostOrder()
    {
        var storage = new SnapshotStorage();
        var builder = new PostSnapshotBuilder(new VectorSource([Post(10, 10.25f), Post(2, 2.25f)]), storage);
        var request = new SnapshotResetJob(-1001234567890, DateTimeOffset.UtcNow);
        await builder.RebuildAsync(request, default);

        Assert.Equal("-1001234567890", storage.Name);
        Assert.Equal(request.RequestedAt, storage.RequestedAt);
        var bytes = Assert.IsType<byte[]>(storage.Bytes);
        Assert.Equal(SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)), bytes[^32..]);
        using var reader = new BinaryReader(new MemoryStream(bytes));
        Assert.Equal(0x31534354, reader.ReadInt32());
        Assert.Equal(storage.Name, reader.ReadString());
        Assert.Equal(VectorFormat.Model, reader.ReadString());
        Assert.Equal(VectorFormat.Version, reader.ReadString());
        Assert.Equal(VectorFormat.Dimensions, reader.ReadInt32());
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(2, reader.ReadInt32());
        foreach (var id in new[] { 2, 10 })
        {
            Assert.Equal(id, reader.ReadInt64());
            Assert.Equal($"Post {id}", reader.ReadString());
            Assert.Equal(1, reader.ReadInt32());
        }
        foreach (var coordinate in new[] { 2.25f, 10.25f })
        {
            Assert.Equal(coordinate, reader.ReadSingle());
            reader.BaseStream.Position += (VectorFormat.Dimensions - 1) * sizeof(float);
        }
        Assert.Equal(bytes.Length - 32, reader.BaseStream.Position);
    }

    [Fact]
    public async Task EmptyIndexDoesNotPublishABlob()
    {
        var storage = new SnapshotStorage();
        var builder = new PostSnapshotBuilder(new VectorSource([]), storage);
        await builder.RebuildAsync(new SnapshotResetJob(-1001234567890, DateTimeOffset.UtcNow), default);
        Assert.Null(storage.Bytes);
    }

    private static IndexedPost Post(long id, float coordinate)
    {
        var vector = new float[VectorFormat.Dimensions];
        vector[0] = coordinate;
        return new IndexedPost(id, $"Post {id}", [vector]);
    }

    private sealed class VectorSource(IndexedPost[] posts) : IPostVectorRepository
    {
        public async IAsyncEnumerable<IndexedPost> ListAsync(long chatId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            foreach (var post in posts) yield return post;
        }

        public Task SaveAsync(long chatId, IndexedPost post, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ShouldIndexAsync(long chatId, long messageId, long revision, string fingerprint,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SnapshotStorage : IPostSnapshotRepository
    {
        public string? Name { get; private set; }
        public DateTimeOffset RequestedAt { get; private set; }
        public byte[]? Bytes { get; private set; }

        public async Task PublishAsync(string name, DateTimeOffset requestedAt, Func<Stream, CancellationToken, Task<bool>> write,
            CancellationToken cancellationToken)
        {
            Name = name;
            RequestedAt = requestedAt;
            using var target = new MemoryStream();
            if (await write(target, cancellationToken)) Bytes = target.ToArray();
        }

        public IAsyncEnumerable<SnapshotBlob> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
