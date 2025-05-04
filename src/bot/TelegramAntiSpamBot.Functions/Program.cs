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
.ConfigureLogging(logging =>
{
    logging.Services.Configure<LoggerFilterOptions>(options =>
    {
        var defaultRule = options.Rules.FirstOrDefault(rule => rule.ProviderName
            == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
        if (defaultRule is not null)
        {
            options.Rules.Remove(defaultRule);
        }
    });
});

builder.ConfigureServices(collection =>
{
    collection.AddOpenAiService();
    collection.AddTelegramBot();
    collection.AddPersistence();
});


builder.Build().Run();
