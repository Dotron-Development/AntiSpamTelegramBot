using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramAntiSpamBot.Initializer
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine(args[0]);
            Console.WriteLine(args[1]);
            Console.WriteLine(args[2]);

            var bot = new TelegramBotClient(args[0]);
            await bot.SetWebhook(
                url: args[1],
                allowedUpdates: [UpdateType.Message, UpdateType.EditedMessage],
                secretToken: args[2]);
        }
    }
}
