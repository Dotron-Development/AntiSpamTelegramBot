namespace TelegramAntiSpamBot.Functions
{
    // todo: temp solution. Should be removed as soon as Microsoft fix Flex Consumption Key Vault references
    public class KeyVaultConfiguration
    {
        public required string KeyVaultUrl { get; set; }

        /// <summary>
        /// Optional. If specified, the application will use this Managed Identity to access KeyVault.
        /// </summary>
        public string? KeyVaultIdentityClientId { get; set; }
    }
}
