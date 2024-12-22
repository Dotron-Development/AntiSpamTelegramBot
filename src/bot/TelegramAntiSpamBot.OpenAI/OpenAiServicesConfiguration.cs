using System.ComponentModel.DataAnnotations;

namespace TelegramAntiSpamBot.OpenAI
{
    internal class OpenAiServicesConfiguration
    {
        [Required]
        public required string ServiceUrl { get; init; }
        [Required]
        public required string ImageRecognitionDeployment { get; init; }
        [Required]
        public required string SpamRecognitionDeployment { get; init; }
    }
}
