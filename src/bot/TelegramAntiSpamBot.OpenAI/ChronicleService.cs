
namespace TelegramAntiSpamBot.OpenAI;

internal sealed class ChronicleService(
    [FromKeyedServices("Chronicle")] ChatClient client,
    SpamDetectionInstructions instructions,
    IMessageHistoryRepository historyRepository) : IChronicleService
{
    public async Task<string> GenerateAsync(long chatId, int? excludedMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var to = DateTimeOffset.UtcNow;
        var messages = new Queue<HistoryMessage>(100);
        try
        {
            await foreach (var message in historyRepository.GetAsync(chatId, to.AddHours(-1), to, cancellationToken))
            {
                if (message.MessageId == excludedMessageId)
                    continue;
                if (messages.Count == 100)
                    messages.Dequeue();
                messages.Enqueue(message);
            }
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            return "No messages in the last hour.";
        }
        return await SummarizeAsync(messages.ToArray(), cancellationToken);
    }

    public async Task<string> SummarizeAsync(IReadOnlyList<HistoryMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
            return "No messages in the last hour.";

        var prompt = await instructions.GetChronicleInstructions();
        var content = JsonSerializer.Serialize(new
        {
            Messages = messages
                .OrderBy(message => message.DateTimeTicks)
                .ThenBy(message => message.MessageId)
                .TakeLast(100)
                .Select(message => new
                {
                    message.MessageId,
                    SentAtUtc = new DateTime(message.DateTimeTicks, DateTimeKind.Utc),
                    message.Content
                })
        });
        var summary = await CompleteAsync(prompt, content, cancellationToken);
        if (summary.Length > 4000)
        {
            var length = 3997;

            if (char.IsHighSurrogate(summary[length - 1]))
                length--;

            summary = summary[..length] + "...";
        }
        return summary;
    }

    private async Task<string> CompleteAsync(string prompt, string content, CancellationToken cancellationToken)
    {
        var completion = await client.CompleteChatAsync(
            [
                new SystemChatMessage(prompt),
                new UserChatMessage(content)
            ],
            new ChatCompletionOptions(), cancellationToken);

        var summary = string.Join("\n", completion.Value.Content.Select(part => part.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text)));

        if (string.IsNullOrWhiteSpace(summary))
            throw new InvalidOperationException("Chronicle AI returned an empty summary.");

        return summary;
    }
}