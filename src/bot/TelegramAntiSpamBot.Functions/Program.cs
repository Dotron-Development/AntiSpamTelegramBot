HostBuilder builder = new();

builder.ConfigureFunctionsWorkerDefaults(b =>
{
    b.UseMiddleware<AuthorizationMiddleware>();
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
