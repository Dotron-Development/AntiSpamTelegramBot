namespace TelegramAntiSpamBot.OpenAI;

public sealed record MentionRequest(string Question, MessageFrom? From, HistoryMessageReply? ReplyTo,
    MentionConversation? Conversation = null);

public sealed record MentionConversation(long ChatId, int MessageId, DateTimeOffset Date, DateTimeOffset? ReplyDate);
