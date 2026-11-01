namespace TelegramAntiSpamBot.Persistence.DTO;

public sealed record MessagePayload(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ImageDescription = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ImageText = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UserMessage = null)
{
    [JsonIgnore]
    public bool HasContent => !string.IsNullOrWhiteSpace(UserMessage)
        || !string.IsNullOrWhiteSpace(ImageText) || !string.IsNullOrWhiteSpace(ImageDescription);

    [JsonIgnore]
    public int ContentLength => (UserMessage?.Length ?? 0) + (ImageText?.Length ?? 0)
        + (ImageDescription?.Length ?? 0);
}
