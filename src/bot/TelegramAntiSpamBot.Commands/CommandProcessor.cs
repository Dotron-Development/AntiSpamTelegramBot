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
                   && commands.Any(c => EqualsPredicate(c.Command, message));
        }

        public async Task ProcessCommand(Update update)
        {
            if (IsCommand(update.Message?.Text))
            {
                var command = commands.First(c => EqualsPredicate(c.Command, update.Message!.Text!));
                try
                {
                    await bot.DeleteMessage(update.Message!.Chat.Id, update.Message.Id);
                }
                catch (ApiRequestException exception) when (exception.ErrorCode == 400 &&
                    string.Equals(exception.Message, "Bad Request: message to delete not found", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Command message {messageId} in chat {chatId} is already absent",
                        update.Message!.Id, update.Message.Chat.Id);
                }
                if (!await calls.TryAcquireAsync(update.Message.Chat.Id, command.Command, DateTimeOffset.UtcNow,
                    configuration.CommandCooldown))
                {
                    logger.LogInformation("Skipping command {command} for chat {chatId}: cooldown {cooldown} is active",
                        command.Command, update.Message.Chat.Id, configuration.CommandCooldown);
                    return;
                }
                await command.ProcessAsync(update);
            }
        }

        private bool EqualsPredicate(string command, string message) =>
            command.Equals(message, StringComparison.InvariantCultureIgnoreCase) ||
            $"{command}{configuration.BotName}".Equals(message, StringComparison.InvariantCultureIgnoreCase);
    }
}
