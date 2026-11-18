namespace TelegramAntiSpamBot.Search;

public sealed class PostIndexer(IPostVectorRepository repository, ISearchEmbeddings embeddings)
{
    public async Task IndexAsync(PostIndexJob job, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(job.Text)) return;
        if (job.ChatId == 0 || job.MessageId <= 0 || job.Revision <= 0)
            throw new ArgumentException("Invalid post identifiers or revision.", nameof(job));

        var authorTitle = job.TitleOffset >= 0 && job.TitleLength > 0 && job.TitleOffset <= job.Text.Length - job.TitleLength
            ? job.Text.Substring(job.TitleOffset, job.TitleLength)
            : null;
        var (title, inputs) = PostChunks.Prepare(job.Text, authorTitle);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Recipe = "paragraphs-v1", VectorFormat.Model, VectorFormat.Version, title, inputs });
        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        if (!await repository.ShouldIndexAsync(job.ChatId, job.MessageId, job.Revision, fingerprint, cancellationToken)) return;

        var vectors = new List<ReadOnlyMemory<float>>(inputs.Count);
        foreach (var batch in inputs.Chunk(16))
            vectors.AddRange(await embeddings.GenerateAsync(batch, cancellationToken));

        await repository.SaveAsync(job.ChatId, new IndexedPost(job.MessageId, title, vectors)
        {
            Revision = job.Revision,
            Fingerprint = fingerprint
        }, cancellationToken);
    }
}
