namespace TelegramAntiSpamBot.Commands.Commands;

internal sealed class ReportCommand(
    IMessageCountRepository messageCountRepository,
    IReportedMessageProcessor messageProcessor,
    ILogger<ReportCommand> logger) : ICommand
{
    public string Command => "/report";

    public async Task ProcessAsync(Update update)
    {
        if (update.Message is not { From: { IsBot: false } reporter, SenderChat: null, ReplyToMessage: { } target } message ||
            target.Chat.Id != message.Chat.Id || target.From is null || target.SenderChat is not null)
            return;

        var messageCount = await messageCountRepository.GetUserMessageCount(message.Chat.Id, reporter.Id);
        if (messageCount <= 30)
        {
            logger.LogInformation("Ignoring report from untrusted user {userId} in chat {chatId}", reporter.Id, message.Chat.Id);
            return;
        }

        await messageProcessor.ProcessReportedAsync(target);
    }
}
