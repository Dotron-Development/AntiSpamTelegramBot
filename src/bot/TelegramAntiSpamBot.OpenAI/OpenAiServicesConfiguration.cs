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

        /// <summary>
        /// Optional. If specified, the application will use this Managed Identity to access Open AI services.
        /// </summary>
        public string? OpenAiIdentityClientId { get; init; }
    }
}
