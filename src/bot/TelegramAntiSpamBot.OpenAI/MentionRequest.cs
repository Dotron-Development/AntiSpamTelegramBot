namespace TelegramAntiSpamBot.OpenAI;

public sealed record MentionRequest(string Question, HistoryMessageAuthor? From, HistoryMessageReply? ReplyTo);
