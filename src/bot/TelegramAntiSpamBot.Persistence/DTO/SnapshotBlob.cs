namespace TelegramAntiSpamBot.Persistence.DTO;

public sealed record SnapshotBlob(string Name, long SizeBytes, DateTimeOffset LastModified);
