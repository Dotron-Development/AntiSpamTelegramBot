namespace TelegramAntiSpamBot.Persistence.DTO;

public sealed record ReviewChat(long ChatId, string Name, DateTimeOffset LastSeenUtc);

public sealed record ReviewChatPage(IReadOnlyList<ReviewChat> Chats, string? ContinuationToken);