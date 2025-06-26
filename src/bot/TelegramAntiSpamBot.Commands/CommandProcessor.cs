namespace TelegramAntiSpamBot.Commands
{
    internal class CommandProcessor(IEnumerable<ICommand> commands, string botName) : ICommandProcessor
    {
        public bool IsCommand(string? message)
        {
            return !string.IsNullOrEmpty(message) 
                   && commands.Any(c=> EqualsPredicate(c.Command, message));
        }

        public async Task ProcessCommand(Update update)
        {
            if (IsCommand(update.Message?.Text))
            {
                var command = commands.First(c => EqualsPredicate(c.Command, update.Message!.Text!));
                await command.ProcessAsync(update);
            }
        }

        private bool EqualsPredicate(string command, string message) =>
            command.Equals(message, StringComparison.InvariantCultureIgnoreCase) ||
            $"{command}{botName}".Equals(message, StringComparison.InvariantCultureIgnoreCase);
    }
}
