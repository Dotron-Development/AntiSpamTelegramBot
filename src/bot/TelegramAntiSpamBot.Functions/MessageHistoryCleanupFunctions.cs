using TelegramAntiSpamBot.Persistence.Repositories;

namespace TelegramAntiSpamBot.Functions;

public sealed class MessageHistoryCleanupFunctions(
    IMessageHistoryRepository historyRepository,
    ILogger<MessageHistoryCleanupFunctions> logger)
{
    [Function("CleanupMessageHistory")]
    public async Task Run(
        [TimerTrigger("0 0 * * * *", UseMonitor = true)] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var deleted = await historyRepository.TrimAsync(cancellationToken);
        logger.LogInformation("Message history cleanup deleted {count} messages", deleted);
    }
}