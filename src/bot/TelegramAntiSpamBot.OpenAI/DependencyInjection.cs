namespace TelegramAntiSpamBot.OpenAI
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddOpenAiService(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddOptions<OpenAiServicesConfiguration>()
                .BindConfiguration(nameof(OpenAiServicesConfiguration))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            serviceCollection.AddKeyedScoped("SpamDetector", (provider, _) =>
            {
                var config = provider.GetRequiredService<IOptions<OpenAiServicesConfiguration>>();
                var client = new AzureOpenAIClient(
                    new Uri(config.Value.ServiceUrl), 
                    new DefaultAzureCredential());
                var chatClient = client.GetChatClient(config.Value.SpamRecognitionDeployment);
                return chatClient;
            });

            serviceCollection.AddKeyedScoped("ImageAnalyzer", (provider, _) =>
            {
                var config = provider.GetRequiredService<IOptions<OpenAiServicesConfiguration>>();
                var client = new AzureOpenAIClient(
                    new Uri(config.Value.ServiceUrl),
                    new DefaultAzureCredential());
                var chatClient = client.GetChatClient(config.Value.ImageRecognitionDeployment);
                return chatClient;
            });

            serviceCollection.AddSingleton<SpamDetectionInstructions>();
            serviceCollection.AddScoped<ISpamDetectionService, SpamDetectionService>();
            return serviceCollection;
        }
    }
}
