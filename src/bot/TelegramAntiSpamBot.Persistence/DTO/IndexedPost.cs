namespace TelegramAntiSpamBot.Persistence.DTO;

public sealed record IndexedPost(long MessageId, string Title, IReadOnlyList<ReadOnlyMemory<float>> Vectors)
{
    public long? Revision { get; init; }
    public string? Fingerprint { get; init; }
}
