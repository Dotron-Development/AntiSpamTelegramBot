using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace TelegramAntiSpamBot.OpenAI;

internal sealed class MentionService(
    [FromKeyedServices("Mention")] ResponsesClient client,
    SpamDetectionInstructions instructions,
    IOptions<OpenAiServicesConfiguration> configuration) : IMentionService
{
    public async Task<MentionResponse> GenerateAsync(MentionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prompt = await instructions.GetMentionInstructions();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var agent = CreateAgent(prompt);
        var response = await agent.RunAsync(
            JsonSerializer.Serialize(request with { Context = request.Context ?? [] }), cancellationToken: timeout.Token);
        if (response.AsChatResponse().RawRepresentation is ResponseResult result && result.Status != ResponseStatus.Completed)
            throw new InvalidOperationException($"Mention agent response did not complete: {result.Status}.");
        var answer = response.Text;

        if (string.IsNullOrWhiteSpace(answer))
            throw new InvalidOperationException("Mention AI returned an empty answer.");

        return FormatCitations(response);
    }

    private static MentionResponse FormatCitations(AgentResponse response)
    {
        var answer = response.Text;
        var citations = new List<(int Start, int End, string Title, string Url)>();
        var contentOffset = 0;
        foreach (var content in response.Messages.SelectMany(message => message.Contents).OfType<TextContent>())
        {
            var text = content.Text;
            var offset = answer.IndexOf(text, contentOffset, StringComparison.Ordinal);
            if (offset < 0)
                throw new InvalidOperationException("Unable to locate cited content in the agent answer.");
            contentOffset = offset + text.Length;
            foreach (var citation in (content.Annotations ?? []).OfType<CitationAnnotation>())
            {
                if (citation.Url is not { } url)
                    continue;
                var sourceUrl = url.OriginalString;
                var title = string.IsNullOrWhiteSpace(citation.Title) ? sourceUrl : citation.Title;
                var regions = (citation.AnnotatedRegions ?? []).OfType<TextSpanAnnotatedRegion>().ToArray();
                if (regions.Length == 0)
                    citations.Add((contentOffset, contentOffset, title, sourceUrl));
                foreach (var region in regions)
                {
                    if (region.StartIndex is not { } start || region.EndIndex is not { } end ||
                        start < 0 || end < start || end > text.Length ||
                        (end > 0 && end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])))
                        throw new InvalidOperationException("Agent citation contains an invalid text range.");

                    var citedText = text[start..end];
                    // Provider citation markers are replaced; cited prose must remain intact.
                    var marker = (citedText.StartsWith("\uE200cite\uE202", StringComparison.Ordinal) &&
                                  citedText.EndsWith("\uE201", StringComparison.Ordinal)) ||
                                 (citedText.StartsWith("[", StringComparison.Ordinal) &&
                                  citedText.EndsWith($"]({sourceUrl})", StringComparison.Ordinal));
                    citations.Add((offset + (marker ? start : end), offset + end, title, sourceUrl));
                }
            }
        }

        var urls = new List<string>();
        var formatted = new StringBuilder();
        var position = 0;
        foreach (var group in citations.OrderBy(citation => citation.Start).ThenBy(citation => citation.End)
                     .GroupBy(citation => (citation.Start, citation.End)))
        {
            if (group.Key.Start < position)
                throw new InvalidOperationException("Agent citation ranges overlap.");
            formatted.Append(answer.AsSpan(position, group.Key.Start - position));
            var references = new List<(string Title, string Url)>();
            foreach (var citation in group)
            {
                if (!urls.Contains(citation.Url))
                    urls.Add(citation.Url);
                if (!references.Any(reference => reference.Url == citation.Url))
                    references.Add((citation.Title, citation.Url));
            }
            if (formatted.Length > 0 && !char.IsWhiteSpace(formatted[^1]))
                formatted.Append(' ');
            formatted.AppendJoin(' ', references.Select(reference =>
            {
                var label = reference.Title.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]");
                var target = reference.Url.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                return $"[{label}]({target})";
            }));
            position = group.Key.End;
        }
        formatted.Append(answer.AsSpan(position));
        return new MentionResponse(formatted.ToString(), urls);
    }

    private AIAgent CreateAgent(string prompt) =>
        client.AsIChatClient(configuration.Value.MentionDeployment)
            .AsAIAgent(new ChatClientAgentOptions
            {
                Name = "MentionAgent",
                ChatOptions = new ChatOptions
                {
                    Instructions = prompt,
                    MaxOutputTokens = 2000,
                    Tools = [new HostedWebSearchTool()],
                    RawRepresentationFactory = _ => new CreateResponseOptions
                    {
                        StoredOutputEnabled = false,
                        MaxToolCallCount = 3
                    }
                }
            });
}
