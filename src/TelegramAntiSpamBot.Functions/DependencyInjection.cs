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

            serviceCollection.AddScoped(provider =>
            {
                var configuration = provider.GetRequiredService<IOptions<TelegramBotConfiguration>>().Value;
                return new TelegramBotClient(configuration.Token);
            });

            return serviceCollection;
        }
    }
}
