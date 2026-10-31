using TelegramAntiSpamBot.Persistence.DTO;

namespace TelegramAntiSpamBot.OpenAI;

public interface IChronicleService
{
    Task<string> GenerateAsync(long chatId, int? excludedMessageId = null, CancellationToken cancellationToken = default);
    Task<string> SummarizeAsync(IReadOnlyList<HistoryMessage> messages, CancellationToken cancellationToken = default);
}