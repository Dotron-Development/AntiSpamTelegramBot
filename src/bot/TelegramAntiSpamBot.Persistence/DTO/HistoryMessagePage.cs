namespace TelegramAntiSpamBot.Persistence.DTO;

/// <summary>A page of retained incoming messages with an opaque continuation token.</summary>
public sealed record HistoryMessagePage(IReadOnlyList<HistoryMessage> Messages, string? ContinuationToken);