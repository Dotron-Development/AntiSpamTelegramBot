
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

        [Range(typeof(TimeSpan), "00:00:00", "365.00:00:00", ErrorMessage = "SpamRestrictionTimeout must be between zero and 365 days.", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
        public TimeSpan SpamRestrictionTimeout { get; init; } = TimeSpan.FromHours(3);

        [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00", ErrorMessage = "CommandCooldown must be between zero and one day.", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
        public TimeSpan CommandCooldown { get; init; } = TimeSpan.FromMinutes(5);
    }
}
