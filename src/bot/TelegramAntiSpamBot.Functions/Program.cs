HostBuilder builder = new();

builder.ConfigureFunctionsWorkerDefaults(b =>
{
    b.UseWhen<AuthorizationMiddleware>(context => context.FunctionDefinition.Name == "AntiSpamBotWebHook");
})
.ConfigureServices(services =>
{
    // services.AddApplicationInsightsTelemetryWorkerService();
});


builder.ConfigureServices(collection =>
{
    collection.AddOpenAiService();
    collection.AddTelegramBot();
    collection.AddPersistence();
    collection.AddCommands();
});


builder.Build().Run();
