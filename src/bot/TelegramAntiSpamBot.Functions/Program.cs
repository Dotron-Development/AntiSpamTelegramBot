HostBuilder builder = new();

builder.ConfigureFunctionsWorkerDefaults(b =>
{
    b.UseWhen<AuthorizationMiddleware>(context => context.FunctionDefinition.Name == "AntiSpamBotWebHook");
    b.UseWhen<ReviewAuthorizationMiddleware>(context => ReviewAuthorizationMiddleware.RequiresTelegramAuthorization(
        context.FunctionDefinition.Name, context.FunctionDefinition.InputBindings.Values.Select(binding => binding.Type)));
})
.ConfigureServices(services =>
{
    // services.AddApplicationInsightsTelemetryWorkerService();
});


builder.ConfigureServices(collection =>
{
    collection.AddOpenAiService();
    collection.AddTelegramBot();
    collection.AddSingleton(TimeProvider.System);
    collection.AddSingleton<TelegramReviewAuth>();
    collection.AddPersistence();
    collection.AddSearch();
    collection.AddSingleton<PostIndexQueue>();
    collection.AddSingleton<SnapshotResetQueue>();
    collection.AddCommands();
});


builder.Build().Run();
