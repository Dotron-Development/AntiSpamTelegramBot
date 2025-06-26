using System.Text;

namespace TelegramAntiSpamBot.Commands.Commands
{
    internal class StatCommand(ITelegramBotClient bot, IAntiSpamBotRepository repository) : ICommand
    {
        private static readonly CompositeFormat MessageFormat = CompositeFormat.Parse(
            """
            Statistics Report
            ----------------
             Messages deleted today: {0}
             Messages deleted this month: {1}
             Max Delay before deletion: {2}ms
             Min Delay before deletion: {3}ms
             Avg Delay before deletion: {4}ms
            ----------------
            """);

        public string Command => "/stat";

        public async Task ProcessAsync(Update update)
        {
            var message = update.Message;
            if (message == null)
                return;

            var chatId = message.Chat.Id;
            var dailyTask = repository.GetDailyStats(chatId);
            var monthlyTask = repository.GetMonthlyStats(chatId);
            var timingsTask = repository.GetTimingsStats(chatId);

            await Task.WhenAll(dailyTask, monthlyTask, timingsTask);

            var dailyStats = dailyTask.Result.Value;
            var monthlyStats = monthlyTask.Result.Value;
            var timing = timingsTask.Result.Value;

            var outputMessage = string.Format(
                null,
                MessageFormat,
                dailyStats,
                monthlyStats,
                timing.MaxDelayMs?.ToString() ?? "<No data> ",
                timing.MinDelayMs?.ToString() ?? "<No data> ",
                timing.AvgDelayMs?.ToString() ?? "<No data> ");

            var sendTask = bot.SendMessage(chatId, outputMessage);
            var deleteTask = bot.DeleteMessage(chatId, message.Id);

            await Task.WhenAll(sendTask, deleteTask);

            var sentMessage = sendTask.Result;

            await Task.Delay(TimeSpan.FromSeconds(15));
            await bot.DeleteMessage(chatId, sentMessage.MessageId);
        }
    }
}
