namespace TelegramAntiSpamBot.Functions;

public sealed class SnapshotFunctions(PostSnapshotBuilder builder)
{
    [Function(nameof(RebuildSearchSnapshot))]
    public Task RebuildSearchSnapshot(
        [QueueTrigger(SnapshotResetJob.QueueName, Connection = PostIndexJob.QueueConnection)] string payload,
        CancellationToken cancellationToken)
    {
        var job = JsonSerializer.Deserialize<SnapshotResetJob>(payload) ?? throw new InvalidDataException("Missing snapshot reset job.");
        return builder.RebuildAsync(job, cancellationToken);
    }
}
