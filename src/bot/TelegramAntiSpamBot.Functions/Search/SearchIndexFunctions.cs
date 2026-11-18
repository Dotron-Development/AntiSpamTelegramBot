namespace TelegramAntiSpamBot.Functions;

public sealed class SearchIndexFunctions(PostIndexer indexer)
{
    [Function(nameof(IndexChannelPost))]
    public Task IndexChannelPost(
        [QueueTrigger(PostIndexJob.QueueName, Connection = PostIndexJob.QueueConnection)] string payload,
        CancellationToken cancellationToken)
    {
        var job = JsonSerializer.Deserialize<PostIndexJob>(payload) ?? throw new InvalidDataException("Missing search indexing job.");
        return indexer.IndexAsync(job, cancellationToken);
    }
}
