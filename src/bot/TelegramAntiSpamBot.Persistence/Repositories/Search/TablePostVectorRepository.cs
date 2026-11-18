namespace TelegramAntiSpamBot.Persistence.Repositories;

internal sealed class TablePostVectorRepository(TableClient table) : IPostVectorRepository
{
    public async Task<bool> ShouldIndexAsync(long chatId, long messageId, long revision, string fingerprint, CancellationToken cancellationToken = default)
    {
        var partition = Key(chatId);
        var rowKey = $"{Key(messageId)}:post";
        var response = await table.GetEntityIfExistsAsync<TableEntity>(partition, rowKey,
            select: ["Revision", "Fingerprint"], cancellationToken: cancellationToken);
        if (!response.HasValue) return true;
        var row = response.Value!;
        if (row.GetInt64("Revision") >= revision) return false;
        if (row.GetString("Fingerprint") != fingerprint) return true;

        await table.UpdateEntityAsync(new TableEntity(partition, rowKey) { ["Revision"] = revision }, row.ETag,
            cancellationToken: cancellationToken);
        return false;
    }

    public async Task SaveAsync(long chatId, IndexedPost post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        if (post.MessageId <= 0 || string.IsNullOrWhiteSpace(post.Title) || post.Title.Length > 32768 || post.Vectors.Count is < 1 or > 99)
            throw new ArgumentException("A post requires a positive ID, a title, and 1–99 chunks.", nameof(post));

        var partition = Key(chatId);
        var id = Key(post.MessageId);
        var titleKey = $"{id}:post";
        var desired = new Dictionary<string, TableEntity>(StringComparer.Ordinal)
        {
            [titleKey] = new(partition, titleKey)
            {
                ["MessageId"] = post.MessageId, ["Title"] = post.Title, ["ChunkCount"] = post.Vectors.Count,
                ["Dimensions"] = VectorFormat.Dimensions, ["Model"] = VectorFormat.Model, ["ModelVersion"] = VectorFormat.Version
            }
        };
        if (post.Revision is { } revision) desired[titleKey]["Revision"] = revision;
        if (post.Fingerprint is { } fingerprint) desired[titleKey]["Fingerprint"] = fingerprint;
        for (var i = 0; i < post.Vectors.Count; i++)
        {
            var row = new TableEntity(partition, ChunkKey(id, i)) { ["Vector"] = VectorFormat.Encode(post.Vectors[i].Span) };
            desired.Add(row.RowKey, row);
        }

        var actions = new List<TableTransactionAction>();
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and RowKey ge {id + ":"} and RowKey lt {id + ";"}");
        await foreach (var row in table.QueryAsync<TableEntity>(filter, select: ["PartitionKey", "RowKey", "Revision"], cancellationToken: cancellationToken))
        {
            if (row.RowKey == titleKey && post.Revision is { } incoming && row.GetInt64("Revision") >= incoming) return;
            if (desired.Remove(row.RowKey, out var replacement))
                actions.Add(new(TableTransactionActionType.UpdateReplace, replacement, row.ETag));
            else
                actions.Add(new(TableTransactionActionType.Delete, row, row.ETag));
        }
        actions.AddRange(desired.Values.Select(row => new TableTransactionAction(TableTransactionActionType.Add, row)));
        if (actions.Count > 100) throw new InvalidOperationException("Post replacement exceeds the Azure Table transaction limit.");
        await table.SubmitTransactionAsync(actions, cancellationToken);
    }

    public async IAsyncEnumerable<IndexedPost> ListAsync(long chatId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var partition = Key(chatId);
        var chunks = new List<ReadOnlyMemory<float>>();
        string? pendingId = null;
        await foreach (var row in table.QueryAsync<TableEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {partition}"),
            cancellationToken: cancellationToken))
        {
            var separator = row.RowKey.IndexOf(':');
            if (separator <= 0) throw new InvalidDataException("Invalid vector row key.");
            var id = row.RowKey[..separator];
            if (pendingId is not null && pendingId != id) throw new InvalidDataException("Missing post metadata in the vector index.");
            pendingId = id;
            if (row.RowKey == $"{id}:post")
            {
                if (row.GetInt64("MessageId") is not long messageId || messageId <= 0 || Key(messageId) != id ||
                    row.GetInt32("ChunkCount") != chunks.Count || chunks.Count == 0 ||
                    row.GetInt32("Dimensions") != VectorFormat.Dimensions || row.GetString("Model") != VectorFormat.Model ||
                    row.GetString("ModelVersion") != VectorFormat.Version || row.GetString("Title") is not { Length: > 0 } title)
                    throw new InvalidDataException("Invalid post metadata in the vector index.");
                yield return new IndexedPost(messageId, title, chunks.ToArray());
                chunks.Clear();
                pendingId = null;
            }
            else
            {
                if (row.RowKey != ChunkKey(id, chunks.Count) || row.GetBinary("Vector") is not { } bytes)
                    throw new InvalidDataException("Missing or out-of-order vector chunk.");
                chunks.Add(VectorFormat.Decode(bytes));
            }
        }
        if (pendingId is not null) throw new InvalidDataException("Missing post metadata in the vector index.");
    }

    private static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);
    private static string ChunkKey(string id, int i) => string.Create(CultureInfo.InvariantCulture, $"{id}:chunk:{i:D4}");
}
