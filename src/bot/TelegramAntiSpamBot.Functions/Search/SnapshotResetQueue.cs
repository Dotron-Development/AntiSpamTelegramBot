namespace TelegramAntiSpamBot.Functions;

public sealed class SnapshotResetQueue(IConfiguration configuration)
{
    private QueueClient? client;

    public async Task EnqueueAsync(long chatId, CancellationToken cancellationToken, TimeSpan? delay = null)
    {
        var queue = LazyInitializer.EnsureInitialized(ref client, () => PostIndexQueue.CreateClient(configuration, SnapshotResetJob.QueueName));
        var job = new SnapshotResetJob(chatId, DateTimeOffset.UtcNow);
        await queue.SendMessageAsync(JsonSerializer.Serialize(job), visibilityTimeout: delay, cancellationToken: cancellationToken);
    }
}
