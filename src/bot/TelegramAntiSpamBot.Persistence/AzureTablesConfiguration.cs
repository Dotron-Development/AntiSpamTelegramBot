using System.ComponentModel.DataAnnotations;

namespace TelegramAntiSpamBot.Persistence
{
    public class AzureTablesConfiguration
    {
        [Required]
        public required string StorageAccountUrl { get; init; }

        public string? TableIdentityClientId { get; init; }

    }
}
