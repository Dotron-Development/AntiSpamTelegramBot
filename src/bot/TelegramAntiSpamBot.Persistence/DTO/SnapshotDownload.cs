namespace TelegramAntiSpamBot.Persistence.DTO;

// Null content denotes a conditional download that was not modified.
public sealed record SnapshotDownload(string ETag, long Length, Stream? Content);
