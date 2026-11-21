namespace TelegramAntiSpamBot.Search;

public static class SearchRegistration
{
    public static IServiceCollection AddSearch(this IServiceCollection services)
    {
        services.AddSingleton<ISearchEmbeddings, AzureSearchEmbeddings>();
        services.AddScoped<PostIndexer>();
        services.AddScoped<PostSnapshotBuilder>();
        services.AddSingleton<PostSearch>();
        return services;
    }
}
