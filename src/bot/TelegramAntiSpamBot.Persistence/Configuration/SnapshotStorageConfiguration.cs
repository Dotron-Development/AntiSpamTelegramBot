namespace TelegramAntiSpamBot.Persistence;

internal sealed class SnapshotStorageConfiguration
{
    [Required]
    public required string ServiceUrl { get; init; }
    [Required]
    public required string ContainerName { get; init; }
    public string? IdentityClientId { get; init; }
}
