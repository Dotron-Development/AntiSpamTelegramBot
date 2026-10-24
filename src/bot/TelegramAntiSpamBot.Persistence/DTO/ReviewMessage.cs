namespace TelegramAntiSpamBot.Persistence.DTO;

public sealed record ReviewMessage(long ChatId, int MessageId, long UserId,
    string UserName, string Message, string ImageText, string ContentHash, long MessageDateTicks);

public sealed record ReviewMessagePage(IReadOnlyList<ReviewMessage> Messages, string? ContinuationToken);