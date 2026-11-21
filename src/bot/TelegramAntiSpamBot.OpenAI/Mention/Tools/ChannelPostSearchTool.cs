namespace TelegramAntiSpamBot.OpenAI;

internal sealed class ChannelPostSearchTool(
    IChannelPostSearch search,
    long chatId,
    CancellationToken cancellationToken,
    ILogger<MentionService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(AIJsonUtilities.DefaultOptions)
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter<ChannelPostSearchStatus>() }
    };

    public AITool Create() => AIFunctionFactory.Create(SearchAsync, name: "search_channel_posts", serializerOptions: SerializerOptions);

    [Description("Search indexed posts in the Telegram channel linked to the current chat. Returns up to five titles and original post URLs, not full text. Completed with no posts means no results; NoLinkedChannel and IndexNotReady mean the search is unavailable.")]
    private async Task<ChannelPostSearchResult> SearchAsync(
        [Description("Concise topic, keywords, or remembered title of the post (maximum 4096 characters).")]
        string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        query = query.Trim();
        if (query.Length > 4096)
            throw new ArgumentException("Search query must contain at most 4096 characters.", nameof(query));

        try
        {
            return await search.FindAsync(chatId, query, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Unable to search channel posts for chat {ChatId}", chatId);
            throw;
        }
    }
}
