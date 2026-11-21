namespace TelegramAntiSpamBot.Commands.Commands;

internal sealed class SearchCommand(
    ITelegramBotClient bot,
    IChannelPostSearch postSearch,
    IOptions<TelegramBotConfiguration> configuration,
    ILogger<SearchCommand> logger) : ICommand
{
    public string Command => "/search";
    public bool DeleteInvocation => false;
    public bool IsCoolDownCommand => false;

    public bool Matches(string message, string botName) =>
        SlashCommandSyntax.MatchLength(message, Command, botName, acceptsArguments: true) != 0;

    public async Task ProcessAsync(Update update)
    {
        if (update.Message is not { Text: { } text } message) return;
        var commandLength = SlashCommandSyntax.MatchLength(text, Command, configuration.Value.BotName, acceptsArguments: true);
        if (commandLength == 0) return;
        var query = text[commandLength..].Trim();
        var response = query.Length == 0 || message.Chat.Type is not (ChatType.Group or ChatType.Supergroup)
            ? null
            : await SearchAsync(message.Chat.Id, query);
        if (response is null)
        {
            try
            {
                await bot.DeleteMessage(message.Chat.Id, message.Id);
            }
            catch (ApiRequestException exception)
            {
                logger.LogWarning(exception, "Unable to delete search command {MessageId} in chat {ChatId}", message.Id, message.Chat.Id);
            }
            return;
        }

        await bot.SendMessage(message.Chat.Id, response, parseMode: ParseMode.Html,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            messageThreadId: message.IsTopicMessage ? message.MessageThreadId : null,
            replyParameters: new ReplyParameters { MessageId = message.Id, AllowSendingWithoutReply = true });
    }

    private async Task<string?> SearchAsync(long chatId, string query)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var result = await postSearch.FindAsync(chatId, query, timeout.Token);
            return result.Status switch
            {
                ChannelPostSearchStatus.IndexNotReady => "The channel's search index isn't ready yet.",
                ChannelPostSearchStatus.Completed => result.Posts.Count == 0 ? "No results found." : Format(result.Posts),
                _ => null
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Search failed for chat {ChatId}", chatId);
            return null;
        }
    }

    internal static string Format(IReadOnlyList<ChannelPostLink> posts) =>
        string.Join('\n', posts.Select((post, index) =>
            $"{index + 1}. <a href=\"{post.Url}\">{WebUtility.HtmlEncode(post.Title)}</a>"));
}
