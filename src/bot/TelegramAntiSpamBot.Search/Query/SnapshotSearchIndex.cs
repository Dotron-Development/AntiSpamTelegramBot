namespace TelegramAntiSpamBot.Search;

internal sealed class SnapshotSearchIndex((PostSearchResult Result, int EndChunk)[] posts, float[] vectors)
{
    public static async Task<SnapshotSearchIndex> ReadAsync(Stream source, string channel, long length, CancellationToken ct)
    {
        const int vectorSize = VectorFormat.Dimensions * sizeof(float);
        const int checksumSize = 32;
        using var header = new MemoryStream();
        using (var writer = new BinaryWriter(header, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(SnapshotMagic.Value);
            writer.Write(channel);
            writer.Write(VectorFormat.Model);
            writer.Write(VectorFormat.Version);
            writer.Write(VectorFormat.Dimensions);
        }
        var expectedHeader = header.ToArray();
        var prefix = new byte[expectedHeader.Length + 2 * sizeof(int)];
        await source.ReadExactlyAsync(prefix, ct);
        if (!prefix.AsSpan(0, expectedHeader.Length).SequenceEqual(expectedHeader))
            throw new InvalidDataException("Unexpected snapshot format, channel or model.");
        var postCount = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(expectedHeader.Length));
        var chunkCount = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(expectedHeader.Length + sizeof(int)));
        var metadataBytes = length - prefix.Length - (long)chunkCount * vectorSize - checksumSize;
        if (postCount <= 0 || chunkCount < postCount || metadataBytes < 14L * postCount || metadataBytes > 16 * 1024 * 1024)
            throw new InvalidDataException("Invalid snapshot counts.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(prefix);
        var metadata = new byte[(int)metadataBytes];
        await source.ReadExactlyAsync(metadata, ct);
        hash.AppendData(metadata);
        using var metadataReader = new BinaryReader(new MemoryStream(metadata));
        var posts = new (PostSearchResult, int)[postCount];
        var count = 0;
        long previous = 0;
        for (var i = 0; i < posts.Length; i++)
        {
            var id = metadataReader.ReadInt64();
            var title = metadataReader.ReadString();
            var chunks = metadataReader.ReadInt32();
            if (id <= previous || string.IsNullOrWhiteSpace(title) || chunks <= 0 || chunks > chunkCount - count)
                throw new InvalidDataException("Invalid snapshot post metadata.");
            count += chunks;
            posts[i] = (new PostSearchResult(id, title), count);
            previous = id;
        }
        if (count != chunkCount || metadataReader.BaseStream.Position != metadataBytes)
            throw new InvalidDataException("Unexpected snapshot metadata length.");

        var vectors = new float[checked(chunkCount * VectorFormat.Dimensions)];
        var buffer = new byte[vectorSize * 32];
        for (var offset = 0; offset < vectors.Length;)
        {
            var size = Math.Min(buffer.Length, (vectors.Length - offset) * sizeof(float));
            await source.ReadExactlyAsync(buffer.AsMemory(0, size), ct);
            hash.AppendData(buffer.AsSpan(0, size));
            if (BitConverter.IsLittleEndian)
                Buffer.BlockCopy(buffer, 0, vectors, offset * sizeof(float), size);
            else
                for (var i = 0; i < size / sizeof(float); i++)
                    vectors[offset + i] = BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(i * sizeof(float)));
            offset += size / sizeof(float);
        }
        var expected = new byte[checksumSize];
        await source.ReadExactlyAsync(expected, ct);
        if (!hash.GetHashAndReset().AsSpan().SequenceEqual(expected)) throw new InvalidDataException("Snapshot checksum mismatch.");
        return new SnapshotSearchIndex(posts, vectors);
    }

    public IReadOnlyList<PostSearchResult> Find(ReadOnlySpan<float> query, string text, int limit, CancellationToken ct)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var tags = words.Where(word => word.Length > 1 && word[0] == '#').ToArray();
        var top = new PriorityQueue<PostSearchResult, (float Score, long Id)>();
        var chunk = 0;
        foreach (var (post, end) in posts)
        {
            ct.ThrowIfCancellationRequested();
            var best = float.NegativeInfinity;
            for (; chunk < end; chunk++)
            {
                var score = TensorPrimitives.CosineSimilarity(query, vectors.AsSpan(chunk * VectorFormat.Dimensions, VectorFormat.Dimensions));
                if (float.IsFinite(score)) best = Math.Max(best, score);
            }
            if (!float.IsFinite(best)) continue;
            if (post.Title.Contains(text, StringComparison.OrdinalIgnoreCase)) best += 0.18f;
            else if (tags.Any(tag => post.Title.Contains(tag, StringComparison.OrdinalIgnoreCase))) best += 0.08f;
            var priority = (best, -post.MessageId);
            if (top.Count < limit) top.Enqueue(post, priority);
            else if (top.TryPeek(out _, out var previous) && priority.CompareTo(previous) > 0)
                top.EnqueueDequeue(post, priority);
        }
        var results = new PostSearchResult[top.Count];
        for (var i = results.Length - 1; i >= 0; i--) results[i] = top.Dequeue();
        return results;
    }
}
