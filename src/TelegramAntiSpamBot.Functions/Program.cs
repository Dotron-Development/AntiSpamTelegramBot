using TelegramAntiSpamBot.Functions;

var builder = FunctionsApplication.CreateBuilder(args);
Console.OutputEncoding = Encoding.UTF8;
builder.ConfigureFunctionsWebApplication();
builder.Services.AddOpenAiService();

builder.Services.AddTelegramBot();
builder.Services.AddPersistence();

builder.Build().Run();
