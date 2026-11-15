namespace TelegramAntiSpamBot.OpenAI;

internal sealed record MentionHistoryItem(
    int MessageId,
    DateTimeOffset Date,
    HistoryMessageContent Content,
    HistoryUpdateType UpdateType,
    int? UpdateId);
