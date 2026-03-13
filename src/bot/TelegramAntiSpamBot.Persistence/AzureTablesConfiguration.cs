using System.ComponentModel.DataAnnotations;

namespace TelegramAntiSpamBot.Persistence
{
    public class AzureTablesConfiguration
    {
        [Required]
        public required string StorageAccountUrl { get; init; }

        /// <summary>
        /// Optional. If specified, the application will use this Managed Identity to access Azure Tables.
        /// </summary>
        public string? TableIdentityClientId { get; init; }

    }
}
