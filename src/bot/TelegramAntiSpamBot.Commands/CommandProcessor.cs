using Telegram.Bot.Exceptions;

namespace TelegramAntiSpamBot.Commands
{
    internal class CommandProcessor(
        IEnumerable<ICommand> commands,
        TelegramBotConfiguration configuration,
        ITelegramBotClient bot,
        ICommandCallsRepository calls,
        ILogger<CommandProcessor> logger) : ICommandProcessor
    {
        public bool IsCommand(string? message)
        {
            return !string.IsNullOrEmpty(message)
                   && commands.Any(c => c.Matches(message, configuration.BotName));
        }

        public async Task ProcessCommand(Update update)
        {
            if (update.Message is { Text: { } text } message && IsCommand(text))
            {
                var command = commands.First(c => c.Matches(text, configuration.BotName));
                if (command.DeleteInvocation)
                {
                    try
                    {
                        await bot.DeleteMessage(message.Chat.Id, message.Id);
                    }
                    catch (ApiRequestException exception) when (exception.ErrorCode == 400 &&
                        string.Equals(exception.Message, "Bad Request: message to delete not found", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogInformation("Command message {messageId} in chat {chatId} is already absent",
                            message.Id, message.Chat.Id);
                    }
                }
                if (!await calls.TryAcquireAsync(message.Chat.Id, command.Command, DateTimeOffset.UtcNow,
                    configuration.CommandCooldown))
                {
                    logger.LogInformation("Skipping command {command} for chat {chatId}: cooldown {cooldown} is active",
                        command.Command, message.Chat.Id, configuration.CommandCooldown);
                    return;
                }
                await command.ProcessAsync(update);
            }
        }
    }
}
