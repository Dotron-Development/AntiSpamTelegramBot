namespace TelegramAntiSpamBot.Persistence
{
    public static class DependencyInjection
    {
        private static readonly string[] TableNames = ["Chats", "ProbablySpam", "MessageCount", "SpamHash", "SpamStats", "MessageHistory", "CommandsCall", "Vectors"];

        public static IServiceCollection AddPersistence(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddOptions<AzureTablesConfiguration>()
                .BindConfiguration(nameof(AzureTablesConfiguration))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            serviceCollection.AddOptions<SnapshotStorageConfiguration>()
                .BindConfiguration(nameof(SnapshotStorageConfiguration))
                .ValidateDataAnnotations();
            serviceCollection.AddSingleton<Repositories.IPostSnapshotRepository>(provider =>
            {
                var settings = provider.GetRequiredService<IOptions<SnapshotStorageConfiguration>>().Value;
                var service = settings.ServiceUrl == "UseDevelopmentStorage=true"
                    ? new BlobServiceClient(settings.ServiceUrl)
                    : new BlobServiceClient(new Uri(settings.ServiceUrl), new ManagedIdentityCredential(
                        ManagedIdentityId.FromUserAssignedClientId(settings.IdentityClientId ??
                            throw new InvalidOperationException("Missing snapshot storage identity client ID."))));
                return new Repositories.BlobPostSnapshotRepository(service.GetBlobContainerClient(settings.ContainerName));
            });

            foreach (var tableName in TableNames)
            {
                serviceCollection.AddKeyedScoped(tableName, (provider, _) =>
                {
                    var options = provider.GetRequiredService<IOptions<AzureTablesConfiguration>>();
                    if (options.Value.StorageAccountUrl == "UseDevelopmentStorage=true")
                        return new TableClient(options.Value.StorageAccountUrl, tableName);
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

            serviceCollection.AddScoped<Repositories.IPostVectorRepository>(provider =>
                new Repositories.TablePostVectorRepository(provider.GetRequiredKeyedService<TableClient>("Vectors")));
            serviceCollection.AddTransient<Repositories.IChatsRepository, Repositories.ChatsRepository>();
            serviceCollection.AddTransient<Repositories.IMessageHistoryRepository, Repositories.MessageHistoryRepository>();
            serviceCollection.AddTransient<Repositories.ICommandCallsRepository, Repositories.CommandCallsRepository>();
            serviceCollection.AddTransient<Repositories.IProbablySpamRepository, Repositories.ProbablySpamRepository>();
            serviceCollection.AddTransient<Repositories.IMessageCountRepository, Repositories.MessageCountRepository>();
            serviceCollection.AddTransient<Repositories.ISpamHashRepository, Repositories.SpamHashRepository>();
            serviceCollection.AddTransient<Repositories.ISpamStatsRepository, Repositories.SpamStatsRepository>();
            return serviceCollection;
        }
    }
}
