
namespace TelegramAntiSpamBot.Commands.Commands;

internal sealed class ChronicleCommand(
    ITelegramBotClient bot,
    IChronicleService chronicleService,
    ILogger<ChronicleCommand> logger) : ICommand
{
    public string Command => "/chronicle";

    public async Task ProcessAsync(Update update)
    {
        if (update.Message is not { } message)
            return;

        string summary;
        try
        {
            summary = await chronicleService.GenerateAsync(message.Chat.Id);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to generate chronicle for chat {chatId}", message.Chat.Id);
            return;
        }

        await bot.SendMessage(message.Chat.Id, summary);
    }
}