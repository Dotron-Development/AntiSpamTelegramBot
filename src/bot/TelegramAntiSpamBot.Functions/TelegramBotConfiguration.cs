namespace TelegramAntiSpamBot.Functions
{
    public class TelegramBotConfiguration
    {
        [Required]
        public required string Token { get; init; }
        public required bool DebugAiResponse { get; init; } = false;
    }
}
