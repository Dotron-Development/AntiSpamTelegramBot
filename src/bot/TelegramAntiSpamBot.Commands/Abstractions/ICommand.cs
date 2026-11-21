
namespace TelegramAntiSpamBot.Commands
{
    internal interface ICommand
    {
        public string Command { get; }

        bool DeleteInvocation => true;

        bool Matches(string message, string botName) =>
            SlashCommandSyntax.MatchLength(message, Command, botName) != 0;

        Task ProcessAsync(Update update);
    }
}
