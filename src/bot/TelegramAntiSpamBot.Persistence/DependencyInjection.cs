namespace TelegramAntiSpamBot.Persistence
{
    public static class DependencyInjection
    {
        private static readonly string[] TableNames = ["SpamHistory", "MessageCount", "SpamHash", "SpamStats"];

        public static IServiceCollection AddPersistence(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddOptions<AzureTablesConfiguration>()
                .BindConfiguration(nameof(AzureTablesConfiguration))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            foreach (var tableName in TableNames)
            {
                serviceCollection.AddKeyedScoped(tableName, (provider, _) =>
                {
                    var options = provider.GetRequiredService<IOptions<AzureTablesConfiguration>>();
                    var tableClient = new TableClient(
                        new Uri(options.Value.StorageAccountUrl),
                        tableName,
                        options.Value.TableIdentityClientId != null
                            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions()
                            {
                                ManagedIdentityClientId = options.Value.TableIdentityClientId
                            })
                            : new DefaultAzureCredential());

                    return tableClient;
                });
            }

            serviceCollection.AddTransient<IAntiSpamBotRepository, AntiSpamBotRepository>();
            return serviceCollection;
        }
    }
}
