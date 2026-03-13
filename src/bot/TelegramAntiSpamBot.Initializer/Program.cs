using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace TelegramAntiSpamBot.Initializer
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var bot = new TelegramBotClient(args[0]);
            await bot.SetWebhook(
                url: args[1],
                allowedUpdates: [UpdateType.Message, UpdateType.EditedMessage],
                secretToken: args[2]);
        }
    }
}
