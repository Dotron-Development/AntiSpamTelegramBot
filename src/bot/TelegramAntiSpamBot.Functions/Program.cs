using Microsoft.Extensions.Configuration;
using TelegramAntiSpamBot.Functions;

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
.ConfigureAppConfiguration(configurationBuilder =>
{
    configurationBuilder.AddJsonFile("appsettings.json");
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

// todo: temp solution. Should be removed as soon as Microsoft fix Flex Consumption Key Vault references
builder.ConfigureAppConfiguration((context, configurationBuilder) =>
{
    var kvConfig = new KeyVaultConfiguration();
    context.Configuration.Bind(kvConfig);
    configurationBuilder.AddCustomKeyVault(kvConfig);
});
 
builder.ConfigureServices(collection =>
{
    collection.AddOpenAiService();
    collection.AddTelegramBot();
    collection.AddPersistence();
    collection.AddCommands();
});


builder.Build().Run();
