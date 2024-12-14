namespace TelegramAntiSpamBot.Functions
{
    public class TelegramBotConfiguration
    {
        [Required]
        public required string Token { get; init; }
        [Required]
        public required string SecretHeader { get; init; }
        public required bool DebugAiResponse { get; init; } = false;
        public long? ForwardSpamToChatId { get; set; } 
    }
}
