namespace TelegramAntiSpamBot.Commands;

public interface IReportedMessageProcessor
{
    Task ProcessReportedAsync(Message message);
}
