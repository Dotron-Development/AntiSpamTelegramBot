namespace TelegramAntiSpamBot.Functions;

public sealed class SearchIndexFunctions(PostIndexer indexer, SnapshotResetQueue snapshots)
{
    [Function(nameof(IndexChannelPost))]
    public async Task IndexChannelPost(
        [QueueTrigger(PostIndexJob.QueueName, Connection = PostIndexJob.QueueConnection)] string payload,
        CancellationToken cancellationToken)
    {
        var job = JsonSerializer.Deserialize<PostIndexJob>(payload) ?? throw new InvalidDataException("Missing search indexing job.");
        if (string.IsNullOrWhiteSpace(job.Text)) return;
        await indexer.IndexAsync(job, cancellationToken);
        // Also notify on retry if the vectors were committed but the previous enqueue failed.
        await snapshots.EnqueueAsync(job.ChatId, cancellationToken, delay: TimeSpan.FromMinutes(1));
    }
}
