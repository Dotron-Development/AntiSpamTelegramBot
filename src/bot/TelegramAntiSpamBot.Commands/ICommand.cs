
namespace TelegramAntiSpamBot.Commands
{
    internal interface ICommand
    {
        public string Command { get; }

        Task ProcessAsync(Update update);
    }
}
