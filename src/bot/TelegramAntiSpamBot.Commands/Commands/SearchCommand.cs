namespace TelegramAntiSpamBot.Commands.Commands;

internal sealed class SearchCommand(
    ITelegramBotClient bot,
    IServiceProvider services,
    IOptions<TelegramBotConfiguration> configuration,
    ILogger<SearchCommand> logger) : ICommand
{
    public string Command => "/search";
    public bool DeleteInvocation => false;

    public bool Matches(string message, string botName)
    {
        var command = Token(message);
        if (command.Equals(Command.AsSpan(), StringComparison.OrdinalIgnoreCase)) return true;
        var username = botName.AsSpan().TrimStart('@');
        return !username.IsEmpty && command.Length > Command.Length && command[Command.Length] == '@' &&
            command[..Command.Length].Equals(Command.AsSpan(), StringComparison.OrdinalIgnoreCase) &&
            command[(Command.Length + 1)..].Equals(username, StringComparison.OrdinalIgnoreCase);
    }

    public async Task ProcessAsync(Update update)
    {
        if (update.Message is not { Text: { } text } message || !Matches(text, configuration.Value.BotName)) return;
        var query = text[Token(text).Length..].Trim();
        if (query.Length == 0)
        {
            await Reply("Использование: /search &lt;запрос&gt;");
            return;
        }
        if (message.Chat.Type is not (ChatType.Group or ChatType.Supergroup))
        {
            await Reply("Поиск доступен в группе обсуждения канала.");
            return;
        }

        string response;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var ct = timeout.Token;
            var group = await bot.GetChat(message.Chat.Id, ct);
            if (group.Type is not (ChatType.Group or ChatType.Supergroup) ||
                group.LinkedChatId is not (< -100 and var channelId) ||
                !channelId.ToString(CultureInfo.InvariantCulture).StartsWith("-100", StringComparison.Ordinal))
            {
                response = "К этой группе не привязан канал.";
            }
            else
            {
                var hits = await services.GetRequiredService<PostSearch>().FindAsync(channelId, query, cancellationToken: ct);
                response = hits switch
                {
                    null => "Индекс канала пока не готов.",
                    { Count: 0 } => "Ничего не найдено.",
                    _ => Format(hits, channelId)
                };
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Search failed for chat {ChatId}", message.Chat.Id);
            response = "Поиск временно недоступен. Попробуйте позже.";
        }
        await Reply(response);

        Task<Message> Reply(string body) => bot.SendMessage(message.Chat.Id, body, parseMode: ParseMode.Html,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            messageThreadId: message.IsTopicMessage ? message.MessageThreadId : null,
            replyParameters: new ReplyParameters { MessageId = message.Id, AllowSendingWithoutReply = true });
    }

    private static ReadOnlySpan<char> Token(string message)
    {
        var end = 0;
        while (end < message.Length && !char.IsWhiteSpace(message[end])) end++;
        return message.AsSpan(0, end);
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
