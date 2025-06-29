using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;

namespace TelegramAntiSpamBot.Functions
{
    // todo: temp solution. Should be removed as soon as Microsoft fix Flex Consumption Key Vault references
    public class KeyVaultConfigurationProvider(KeyVaultConfiguration configuration) : ConfigurationProvider
    {
        public override void Load()
        {
            var client = new SecretClient(new Uri(configuration.KeyVaultUrl), new DefaultAzureCredential(new DefaultAzureCredentialOptions()
            {
                ManagedIdentityClientId = configuration.KeyVaultIdentityClientId
            }));

            // Fetch secrets from Key Vault
            var telegramBotToken = client.GetSecret("telegram-bot-token").Value.Value;
            var telegramBotSecretHeader = client.GetSecret("telegram-bot-secret-header").Value.Value;

            // Map secrets to desired IConfiguration keys
            Data["TelegramBotConfiguration:Token"] = telegramBotToken;
            Data["TelegramBotConfiguration:SecretHeader"] = telegramBotSecretHeader;
        }
    }

    public class KeyVaultConfigurationSource(KeyVaultConfiguration configuration) : IConfigurationSource
    {
        private readonly KeyVaultConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new KeyVaultConfigurationProvider(_configuration);
        }
    }

    public static class KeyVaultConfigurationExtensions
    {
        public static IConfigurationBuilder AddCustomKeyVault(this IConfigurationBuilder builder, KeyVaultConfiguration configuration)
        {
            return builder.Add(new KeyVaultConfigurationSource(configuration));
        }
    }
}
