namespace TelegramAntiSpamBot.Functions;

/// <summary>Metadata for a published channel search snapshot.</summary>
internal sealed record SnapshotResponse(string Name, long SizeBytes, DateTimeOffset LastModified);
