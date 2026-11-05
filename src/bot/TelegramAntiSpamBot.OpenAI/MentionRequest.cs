namespace TelegramAntiSpamBot.OpenAI;

public sealed record MentionRequest(string Question, HistoryMessageAuthor? From, HistoryMessageReply? ReplyTo,
    IReadOnlyList<HistoryMessage>? Context = null);
