namespace TelegramAntiSpamBot.OpenAI;

internal sealed record MentionHistoryResult(IReadOnlyList<MentionHistoryItem> Messages, string? ContinuationToken);
