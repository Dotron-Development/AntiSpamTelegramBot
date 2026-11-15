namespace TelegramAntiSpamBot.Commands;

public interface ICommandProcessor
{
    bool IsCommand(string? message);
    Task ProcessCommand(Update update);
}