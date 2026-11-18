namespace TelegramAntiSpamBot.Functions;

public sealed class PostIndexQueue(IConfiguration configuration)
{
    private QueueClient? client;

    public async Task EnqueueAsync(PostIndexJob job, CancellationToken cancellationToken)
    {
        var queue = LazyInitializer.EnsureInitialized(ref client, CreateClient);
        await queue.SendMessageAsync(JsonSerializer.Serialize(job), timeToLive: TimeSpan.FromDays(1), cancellationToken: cancellationToken);
    }

    private QueueClient CreateClient()
    {
        var connection = PostIndexJob.QueueConnection;
        var settings = configuration.GetSection(connection);
        var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        if (Uri.TryCreate(settings["queueServiceUri"], UriKind.Absolute, out var endpoint))
        {
            var queueUri = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/" + PostIndexJob.QueueName);
            return new QueueClient(queueUri, new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(
                settings["clientId"] ?? throw new InvalidOperationException("Missing search queue identity client ID."))), options);
        }
        return new QueueClient(configuration[connection] ?? throw new InvalidOperationException("Missing search queue connection."),
            PostIndexJob.QueueName, options);
    }
}
