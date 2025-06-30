using Microsoft.Extensions.Configuration;
using TelegramAntiSpamBot.Functions;
using static System.Net.WebRequestMethods;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

IHostBuilder builder = new HostBuilder();


builder.ConfigureFunctionsWorkerDefaults(b =>
{
    b.UseMiddleware<AuthorizationMiddleware>();
})
.ConfigureServices(services =>
{
    services.AddApplicationInsightsTelemetryWorkerService();
    services.ConfigureFunctionsApplicationInsights();
})
.ConfigureAppConfiguration((context, configurationBuilder) =>
{
    configurationBuilder.AddJsonFile("appsettings.json");

    // Build configuration to get KeyVault settings
    var config = configurationBuilder.Build();
    var keyVaultConfig = config.GetSection(nameof(KeyVaultConfiguration)).Get<KeyVaultConfiguration>();

    if (keyVaultConfig != null && !string.IsNullOrEmpty(keyVaultConfig.KeyVaultUrl))
    {
        var credential = !string.IsNullOrEmpty(keyVaultConfig.KeyVaultIdentityClientId)
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions()
            {
                ManagedIdentityClientId = keyVaultConfig.KeyVaultIdentityClientId
            })
            : new DefaultAzureCredential();

        configurationBuilder.AddAzureKeyVault(new Uri(keyVaultConfig.KeyVaultUrl), credential, new Azure.Extensions.AspNetCore.Configuration.Secrets.AzureKeyVaultConfigurationOptions
        {
            Manager = new TelegramBotKeyVaultSecretManager()
        });
    }
})
.ConfigureLogging((context, loggingBuilder) =>
{
    loggingBuilder.Services.Configure<LoggerFilterOptions>((options) =>
    {
        var defaultRule = options.Rules.FirstOrDefault(rule => rule.ProviderName
            == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
        if (defaultRule is not null)
        {
            options.Rules.Remove(defaultRule);
        }
    });

    loggingBuilder.AddConfiguration(context.Configuration.GetSection("Logging"));
});



builder.ConfigureServices(collection =>
{
    collection.AddOpenAiService();
    collection.AddTelegramBot();
    collection.AddPersistence();
    collection.AddCommands();
});


builder.Build().Run();

public class TelegramBotKeyVaultSecretManager : KeyVaultSecretManager
{
    public override string GetKey(KeyVaultSecret secret)
    {
        return secret.Name switch
        {
            "telegram-bot-token" => "TelegramBotConfiguration:Token",
            "telegram-bot-secret-header" => "TelegramBotConfiguration:SecretHeader",
            _ => base.GetKey(secret)
        };
    }
}
