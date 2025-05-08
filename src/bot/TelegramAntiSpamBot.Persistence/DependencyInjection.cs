namespace TelegramAntiSpamBot.Persistence
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddPersistence(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddOptions<AzureTablesConfiguration>()
                .BindConfiguration(nameof(AzureTablesConfiguration))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            serviceCollection.AddKeyedScoped("SpamHistory",(provider, _) =>
            {
                var options = provider.GetRequiredService<IOptions<AzureTablesConfiguration>>();
                var tableClient = new TableClient(
                    new Uri(options.Value.StorageAccountUrl),
                    "SpamHistory",
                   new DefaultAzureCredential());
                return tableClient;
            });

            serviceCollection.AddKeyedScoped("MessageCount", (provider, _) =>
            {
                var options = provider.GetRequiredService<IOptions<AzureTablesConfiguration>>();
                var tableClient = new TableClient(
                    new Uri(options.Value.StorageAccountUrl),
                    "MessageCount",
                   new DefaultAzureCredential());
                return tableClient;
            });

            serviceCollection.AddKeyedScoped("SpamHash", (provider, _) =>
            {
                var options = provider.GetRequiredService<IOptions<AzureTablesConfiguration>>();
                var tableClient = new TableClient(
                    new Uri(options.Value.StorageAccountUrl),
                    "SpamHash",
                    new DefaultAzureCredential());
                return tableClient;
            });

            serviceCollection.AddKeyedScoped("SpamStats", (provider, _) =>
            {
                var options = provider.GetRequiredService<IOptions<AzureTablesConfiguration>>();
                var tableClient = new TableClient(
                    new Uri(options.Value.StorageAccountUrl),
                    "SpamStats",
                    new DefaultAzureCredential());
                return tableClient;
            });

            serviceCollection.AddTransient<IAntiSpamBotRepository, AntiSpamBotRepository>();
            return serviceCollection;
        }
    }
}
