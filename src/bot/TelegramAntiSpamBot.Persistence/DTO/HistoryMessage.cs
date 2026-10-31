namespace TelegramAntiSpamBot.Persistence.DTO;

public sealed record HistoryMessage(long ChatId, int MessageId, long DateTimeTicks, HistoryMessageContent Content);

public sealed record HistoryMessageContent(HistoryMessageAuthor? From, HistoryMessageReply? ReplyTo, string Text);

public sealed record HistoryMessageAuthor(long Id, string Name, string? Username);

public sealed record HistoryMessageReply(int MessageId, HistoryMessageAuthor? From, string Text);