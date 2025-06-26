using System.ComponentModel.DataAnnotations;

namespace TelegramAntispamBot.Configuration
{
    public class TelegramBotConfiguration
    {
        [Required]
        public required string Token { get; init; }
        [Required]
        public required string SecretHeader { get; init; }
        [Required]
        public required string BotName { get; init; }
        public required bool DebugAiResponse { get; init; } = false;
        public long? ForwardSpamToChatId { get; set; }
    }
}
