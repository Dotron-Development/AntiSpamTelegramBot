namespace TelegramAntiSpamBot.Persistence.DTO;

/// <summary>Represents the most recent admitted command call in a chat.</summary>
public sealed record CommandCall(long ChatId, string Command, DateTimeOffset LastCall);

/// <summary>A page of command calls with an opaque continuation token.</summary>
public sealed record CommandCallPage(IReadOnlyList<CommandCall> Calls, string? ContinuationToken);