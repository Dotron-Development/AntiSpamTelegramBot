namespace TelegramAntiSpamBot.Commands.Commands;

internal sealed class SearchCommand(
    ITelegramBotClient bot,
    IServiceProvider services,
    IOptions<TelegramBotConfiguration> configuration,
    ILogger<SearchCommand> logger) : ICommand
{
    public string Command => "/search";
    public bool DeleteInvocation => false;

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
            var ct = timeout.Token;
            var group = await bot.GetChat(chatId, ct);
            if (group.Type is not (ChatType.Group or ChatType.Supergroup) ||
                group.LinkedChatId is not (< -100 and var channelId) ||
                !channelId.ToString(CultureInfo.InvariantCulture).StartsWith("-100", StringComparison.Ordinal))
                return null;

            var hits = await services.GetRequiredService<PostSearch>().FindAsync(channelId, query, cancellationToken: ct);
            return hits switch
            {
                null => "The channel's search index isn't ready yet.",
                { Count: 0 } => "No results found.",
                _ => Format(hits, channelId)
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Search failed for chat {ChatId}", chatId);
            return null;
        }
    }

    internal static string Format(IReadOnlyList<PostSearchResult> hits, long channelId)
    {
        var channel = channelId.ToString(CultureInfo.InvariantCulture)[4..];
        return string.Join('\n', hits.Select((hit, index) =>
        {
            var title = string.Join(' ', hit.Title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (title.Length > 160)
                title = title[..(char.IsHighSurrogate(title[159]) ? 159 : 160)] + "…";
            var id = hit.MessageId.ToString(CultureInfo.InvariantCulture);
            return $"{index + 1}. <a href=\"https://t.me/c/{channel}/{id}\">{WebUtility.HtmlEncode(title)}</a>";
        }));
    }
}
