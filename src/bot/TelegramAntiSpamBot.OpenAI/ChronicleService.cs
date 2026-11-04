
namespace TelegramAntiSpamBot.OpenAI;

/// <summary>
/// Generates a recap from retained chat history.
/// Excludes messages whose text contains /chronicle (case-insensitive), including bot-name suffixes.
/// Collects non-command messages from all history pages, then removes messages preceding
/// [latest non-command message time - 1 hour, latest non-command message time], including both boundaries.
/// Only the latest 100 messages in that window are summarized; older messages do not fill a quiet window.
/// Empty or missing history skips AI. Otherwise, one request sends timestamps, authors, reply context
/// and text to the Chronicle deployment. The result is capped at 4000 characters without splitting
/// surrogate pairs. Command deletion, history persistence and cooldown are handled by callers.
/// </summary>
internal sealed class ChronicleService(
    [FromKeyedServices("Chronicle")] ChatClient client,
    SpamDetectionInstructions instructions,
    IMessageHistoryRepository historyRepository) : IChronicleService
{
    public async Task<string> GenerateAsync(long chatId, CancellationToken cancellationToken = default)
    {
        var messages = new List<HistoryMessage>(100);
        try
        {
            string? continuationToken = null;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await historyRepository.GetPageAsync(chatId, 300, continuationToken, cancellationToken);
                messages.AddRange(page.Messages.Where(message => !ContainsChronicleCommand(message)));
                continuationToken = page.ContinuationToken;
            } while (continuationToken is not null);

            var latestTicks = messages.Max(message => (long?)message.DateTimeTicks);
            if (latestTicks is null)
                return "No messages in the last hour.";

            var fromTicks = latestTicks.Value - TimeSpan.TicksPerHour;
            messages.RemoveAll(message => message.DateTimeTicks < fromTicks);
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            return "No messages in the last hour.";
        }

        return await SummarizeAsync(messages, cancellationToken);
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
                    message.UpdateType,
                    SentAtUtc = new DateTime(message.DateTimeTicks, DateTimeKind.Utc),
                    message.Content
                })
        });

        var summary = await CompleteAsync(prompt, content, cancellationToken);

        return TelegramTextUtils.Truncate(summary);
    }

    private static bool ContainsChronicleCommand(HistoryMessage message) =>
        message.Content.Payload.UserMessage?.Contains("/chronicle", StringComparison.OrdinalIgnoreCase) == true;

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