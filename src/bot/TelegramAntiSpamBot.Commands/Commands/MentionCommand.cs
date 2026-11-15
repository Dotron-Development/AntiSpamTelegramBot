namespace TelegramAntiSpamBot.Commands.Commands;

internal sealed class MentionCommand(
    ITelegramBotClient bot,
    IMentionService mentionService,
    IOptions<TelegramBotConfiguration> configuration,
    ILogger<MentionCommand> logger) : ICommand
{
    public string Command => "@" + configuration.Value.BotName.TrimStart('@');
    public bool DeleteInvocation => false;

    public bool Matches(string message, string botName) =>
        Command.Length > 1 && message.StartsWith(Command, StringComparison.OrdinalIgnoreCase) &&
        (message.Length == Command.Length || char.IsWhiteSpace(message[Command.Length]));

    public async Task ProcessAsync(Update update)
    {
        if (update.Message is not { Text: { } text } message || !Matches(text, configuration.Value.BotName))
            return;

        var request = new MentionRequest(text[Command.Length..].Trim(), GetAuthor(message),
            message.ReplyToMessage is { } reply
                ? new HistoryMessageReply(reply.MessageId, GetAuthor(reply), reply.Text ?? reply.Caption ?? string.Empty)
                : null, new MentionConversation(message.Chat.Id, message.MessageId,
                    new DateTimeOffset(message.Date.ToUniversalTime()),
                    message.ReplyToMessage is { } target ? new DateTimeOffset(target.Date.ToUniversalTime()) : null));

        MentionResponse response;
        try
        {
            response = await mentionService.GenerateAsync(request);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to answer mention for chat {chatId}", message.Chat.Id);
            return;
        }

        await bot.SendMessage(message.Chat.Id, TelegramMarkdownFormatter.Format(response.Answer),
            parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
            replyParameters: new ReplyParameters { MessageId = message.MessageId, AllowSendingWithoutReply = true });
    }

    private static MessageFrom? GetAuthor(Message message)
    {
        if (message.SenderChat is { } chat)
            return new MessageFrom(chat.Id, chat.Title ?? string.Empty, chat.Username);

        if (message.From is { } user)
            return new MessageFrom(user.Id,
                string.Join(" ", new[] { user.FirstName, user.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))),
                user.Username);
        return null;
    }
}
