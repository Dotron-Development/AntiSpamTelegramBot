namespace TelegramAntiSpamBot.OpenAI;

internal sealed class MentionService(
    [FromKeyedServices("Mention")] ChatClient client,
    SpamDetectionInstructions instructions) : IMentionService
{
    public async Task<string> GenerateAsync(MentionRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = await instructions.GetMentionInstructions();
        var completion = await client.CompleteChatAsync(
            [new SystemChatMessage(prompt), new UserChatMessage(JsonSerializer.Serialize(request))],
            new ChatCompletionOptions(), cancellationToken);

        var answer = string.Join("\n", completion.Value.Content.Select(part => part.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text)));

        if (string.IsNullOrWhiteSpace(answer))
            throw new InvalidOperationException("Mention AI returned an empty answer.");

        return TelegramTextUtils.Truncate(answer);
    }
}
