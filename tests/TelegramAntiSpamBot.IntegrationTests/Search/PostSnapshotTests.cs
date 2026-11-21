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

    [Fact]
    public async Task SearchesPublishedSnapshotByBestChunkWithTitleBoostAndReusesCache()
    {
        var storage = new SnapshotStorage();
        var best = Post(2, 1);
        var second = best.Vectors[0].ToArray();
        second[1] = 0.4f;
        var poor = new float[VectorFormat.Dimensions];
        poor[1] = 1;
        var archive = new[] { best, new IndexedPost(10, "Async & <await>", [poor, second]) };
        const long channel = -1001234567890;
        await new PostSnapshotBuilder(new VectorSource(archive), storage)
            .RebuildAsync(new SnapshotResetJob(channel, DateTimeOffset.UtcNow), default);
        var search = new PostSearch(storage, new QueryEmbeddings(best.Vectors[0]));
        Assert.Equal(new long[] { 2, 10 }, (await search.FindAsync(channel, "other"))!.Select(hit => hit.MessageId));
        var results = (await search.FindAsync(channel, "async"))!;
        Assert.Equal(new long[] { 10, 2 }, results.Select(hit => hit.MessageId));
        Assert.Equal(1, storage.Downloads);
        Assert.Contains("https://t.me/c/1234567890/10\">Async &amp; &lt;await&gt;", SearchCommand.Format(results, channel));
    }

    [Theory]
    [InlineData("async", new long[] { 2 })]
    [InlineData("unrelated", new long[] { })]
    [InlineData("async #dotnet", new long[] { })]
    [InlineData("#dotnet", new long[] { 5 })]
    public async Task ReturnsOnlyPostsThatMeetTheRelevanceThreshold(string query, long[] expectedIds)
    {
        var storage = new SnapshotStorage();
        var queryVector = Post(1, 1).Vectors[0];
        var partialMatch = queryVector.ToArray();
        partialMatch[1] = 4;
        var weakMatch = queryVector.ToArray();
        weakMatch[1] = 10;
        var taggedMatch = queryVector.ToArray();
        taggedMatch[1] = 3;
        var archive = new IndexedPost[]
        {
            new(2, "Async basics", [partialMatch]),
            new(3, "Async news", [weakMatch]),
            new(4, "Other topics", [partialMatch]),
            new(5, "Tips #dotnet", [taggedMatch])
        };
        const long channel = -1001234567890;
        await new PostSnapshotBuilder(new VectorSource(archive), storage)
            .RebuildAsync(new SnapshotResetJob(channel, DateTimeOffset.UtcNow), default);
        var search = new PostSearch(storage, new QueryEmbeddings(queryVector));

        var results = await search.FindAsync(channel, query);

        Assert.NotNull(results);
        Assert.Equal(expectedIds, results.Select(hit => hit.MessageId));
    }

    [Fact]
    public async Task RejectsSnapshotWithCorruptChecksum()
    {
        var storage = new SnapshotStorage();
        var request = new SnapshotResetJob(-1001234567890, DateTimeOffset.UtcNow);
        await new PostSnapshotBuilder(new VectorSource([Post(2, 1)]), storage).RebuildAsync(request, default);
        storage.Bytes![^1] ^= 1;
        var search = new PostSearch(storage, new QueryEmbeddings(default));
        await Assert.ThrowsAsync<InvalidDataException>(() => search.FindAsync(request.ChatId, "async"));
    }

    [Theory]
    [InlineData("/search async", true)]
    [InlineData("/SEARCH@TestBot\tasync", true)]
    [InlineData("/search@OtherBot async", false)]
    [InlineData("/searching async", false)]
    public void MatchesOnlySearchCommandsAddressedToThisBot(string text, bool expected)
    {
        var command = new SearchCommand(null!, null!, null!, NullLogger<SearchCommand>.Instance);
        Assert.Equal(expected, command.Matches(text, "@TestBot"));
    }

    [Theory]
    [InlineData("/stat@TestBot", true)]
    [InlineData("/statTestBot", false)]
    public void MatchesExplicitBotNameWhenConfigurationOmitsAtSign(string text, bool expected)
    {
        ICommand command = new StatCommand(null!, null!);
        Assert.Equal(expected, command.Matches(text, "TestBot"));
    }

    [Fact]
    public async Task ProcessesRepeatedSearchWithoutAccessingCooldownStorage()
    {
        using var handler = new SearchDeletionHandler();
        using var http = new HttpClient(handler);
        var bot = new TelegramBotClient("123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk", http);
        var config = new TelegramBotConfiguration { Token = "test", SecretHeader = "test", BotName = "TestBot", DebugAiResponse = false };
        var search = new SearchCommand(bot, null!, Options.Create(config), NullLogger<SearchCommand>.Instance);
        var processor = new CommandProcessor([search], config, bot, null!, NullLogger<CommandProcessor>.Instance);

        foreach (var id in new[] { 1, 2 })
            await processor.ProcessCommand(new Update
            {
                Message = new Message { Id = id, Chat = new Chat { Id = -100 }, Text = "/search" }
            });

        Assert.Equal(new[] { 1, 2 }, handler.Deleted);
    }

    private sealed class SearchDeletionHandler : HttpMessageHandler
    {
        public List<int> Deleted { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.EndsWith("/deleteMessage", request.RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Deleted.Add(body.RootElement.GetProperty("message_id").GetInt32());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":true}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class QueryEmbeddings(ReadOnlyMemory<float> vector) : ISearchEmbeddings
    {
        public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([vector]);
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
        public int Downloads { get; private set; }

        public async Task PublishAsync(string name, DateTimeOffset requestedAt, Func<Stream, CancellationToken, Task<bool>> write,
            CancellationToken cancellationToken)
        {
            Name = name;
            RequestedAt = requestedAt;
            using var target = new MemoryStream();
            if (await write(target, cancellationToken)) Bytes = target.ToArray();
        }

        public IAsyncEnumerable<SnapshotBlob> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SnapshotBlob?> FindAsync(string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SnapshotDownload?> OpenIfChangedAsync(string name, string? etag, CancellationToken cancellationToken)
        {
            Assert.Equal(Name, name);
            Downloads++;
            return Task.FromResult(Bytes is { } bytes ? new SnapshotDownload("test", bytes.Length, new MemoryStream(bytes)) : null);
        }
    }
}
