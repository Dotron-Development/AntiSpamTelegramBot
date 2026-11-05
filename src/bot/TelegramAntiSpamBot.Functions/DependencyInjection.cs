namespace TelegramAntiSpamBot.Functions
{
    internal static class DependencyInjection
    {
        public static IServiceCollection AddTelegramBot(this  IServiceCollection serviceCollection)
        {
            serviceCollection.AddOptions<TelegramBotConfiguration>()
               .BindConfiguration(nameof(TelegramBotConfiguration))
               .ValidateDataAnnotations()
               .ValidateOnStart();

            serviceCollection.AddScoped<SpamMessageProcessor>();
            serviceCollection.AddScoped<IReportedMessageProcessor>(provider => provider.GetRequiredService<SpamMessageProcessor>());

            serviceCollection.AddScoped<ITelegramBotClient>(provider =>
            {
                var configuration = provider.GetRequiredService<IOptions<TelegramBotConfiguration>>().Value;
                return new TelegramBotClient(configuration.Token);
            });

            serviceCollection.AddSingleton(provider =>
            {
                var configuration = provider.GetRequiredService<IOptions<TelegramBotConfiguration>>().Value;
                return new TelegramImageUrlResolver(configuration.Token);
            });

            return serviceCollection;
        }
    }
}
