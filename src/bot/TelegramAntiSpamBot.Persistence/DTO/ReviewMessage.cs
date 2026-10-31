namespace TelegramAntiSpamBot.Persistence.DTO;

public sealed record ReviewMessage(long ChatId, int MessageId, long UserId,
    ReviewMessageFrom From, ReviewMessageReplyTo? ReplyTo, string Message, string ImageText, string ContentHash, long MessageDateTicks);

public sealed record ReviewMessageFrom(string Name, string? Nick);

public sealed record ReviewMessageReplyTo(int MessageId, ReviewMessageFrom? From, string Text);

public sealed record ReviewMessagePage(IReadOnlyList<ReviewMessage> Messages, string? ContinuationToken);