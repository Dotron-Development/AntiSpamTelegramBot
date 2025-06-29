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
                CreateChatClient(provider, config => config.SpamRecognitionDeployment));

            serviceCollection.AddKeyedScoped("ImageAnalyzer", (provider, _) =>
                CreateChatClient(provider, config => config.ImageRecognitionDeployment));

            serviceCollection.AddSingleton<SpamDetectionInstructions>();
            serviceCollection.AddScoped<ISpamDetectionService, SpamDetectionService>();
            return serviceCollection;
        }

        private static ChatClient CreateChatClient(IServiceProvider provider, Func<OpenAiServicesConfiguration, string> deploymentSelector)
        {
            var config = provider.GetRequiredService<IOptions<OpenAiServicesConfiguration>>();
            var client = new AzureOpenAIClient(
                new Uri(config.Value.ServiceUrl),
                config.Value.OpenAiIdentityClientId != null
                    ? new DefaultAzureCredential(new DefaultAzureCredentialOptions()
                    {
                        ManagedIdentityClientId = config.Value.OpenAiIdentityClientId
                    })
                    : new DefaultAzureCredential());
            return client.GetChatClient(deploymentSelector(config.Value));
        }
    }
}
