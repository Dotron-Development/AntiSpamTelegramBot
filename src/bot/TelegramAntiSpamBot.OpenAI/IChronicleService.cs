
namespace TelegramAntiSpamBot.OpenAI;

public interface IChronicleService
{
    Task<string> GenerateAsync(long chatId, CancellationToken cancellationToken = default);
    /// <summary>Summarizes messages already filtered to exclude Chronicle commands.</summary>
    Task<string> SummarizeAsync(IReadOnlyList<HistoryMessage> messages, CancellationToken cancellationToken = default);
}