namespace TelegramAntiSpamBot.Persistence.DTO;

public sealed record ReviewMessage(long ChatId, int MessageId, long UserId,
    MessageFrom From, ReviewMessageReplyTo? ReplyTo, string Message, string ImageText, string ContentHash, long MessageDateTicks);

public sealed record ReviewMessageReplyTo(int MessageId, MessageFrom? From, string Text);

public sealed record ReviewMessagePage(IReadOnlyList<ReviewMessage> Messages, string? ContinuationToken);