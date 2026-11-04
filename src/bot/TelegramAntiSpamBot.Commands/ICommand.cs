
namespace TelegramAntiSpamBot.Commands
{
    internal interface ICommand
    {
        public string Command { get; }

        bool DeleteInvocation => true;

        bool Matches(string message, string botName) =>
            Command.Equals(message, StringComparison.InvariantCultureIgnoreCase) ||
            $"{Command}{botName}".Equals(message, StringComparison.InvariantCultureIgnoreCase);

        Task ProcessAsync(Update update);
    }
}
