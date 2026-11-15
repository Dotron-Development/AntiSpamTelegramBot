namespace TelegramAntiSpamBot.OpenAI
{
    public sealed class ImageAnalysisResult
    {
        public string? TextOnImage { get; init; } = string.Empty;
        public string? ImageDescription { get; init; } = string.Empty;

        public string ToMessageContent() => string.Join("\n", new[] { ImageDescription, TextOnImage }
                                                  .Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
