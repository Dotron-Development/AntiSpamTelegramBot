var builder = new HostBuilder();

builder.ConfigureFunctionsWorkerDefaults();

builder.ConfigureServices(collection =>
{
    collection.AddOpenAiService();
    collection.AddTelegramBot();
    collection.AddPersistence();
});

// configure logging
builder.ConfigureAppConfiguration((_, config) =>
{
    config.AddJsonFile("loggerConfig.json");
}).ConfigureLogging((hostingContext, logging) =>
{
    logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
});



builder.Build().Run();
