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

            serviceCollection.AddKeyedScoped("Chronicle", (provider, _) =>
                CreateChatClient(provider, config => config.ChronicleDeployment));

            serviceCollection.AddKeyedScoped("Mention", (provider, _) =>
                CreateChatClient(provider, config => config.MentionDeployment, CreateMentionClientOptions()));

            serviceCollection.AddSingleton<SpamDetectionInstructions>();
            serviceCollection.AddScoped<ISpamDetectionService, SpamDetectionService>();
            serviceCollection.AddScoped<IChronicleService, ChronicleService>();
            serviceCollection.AddScoped<IMentionService, MentionService>();
            return serviceCollection;
        }

        internal static AzureOpenAIClientOptions CreateMentionClientOptions() => new()
        {
            RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(5)
        };

        private static ChatClient CreateChatClient(IServiceProvider provider, Func<OpenAiServicesConfiguration, string> deploymentSelector,
            AzureOpenAIClientOptions? options = null)
        {
            var config = provider.GetRequiredService<IOptions<OpenAiServicesConfiguration>>();
            AzureOpenAIClient client;

            if (!string.IsNullOrEmpty(config.Value.ApiKey))
            {
                client = new AzureOpenAIClient(
                  new Uri(config.Value.ServiceUrl), new ApiKeyCredential(config.Value.ApiKey), options);
            }
            else
            {
                client = new AzureOpenAIClient(
                    new Uri(config.Value.ServiceUrl),
                    config.Value.OpenAiIdentityClientId != null
                        ? new DefaultAzureCredential(new DefaultAzureCredentialOptions()
                        {
                            ManagedIdentityClientId = config.Value.OpenAiIdentityClientId
                        })
                        : new DefaultAzureCredential(), options);
            }

            return client.GetChatClient(deploymentSelector(config.Value));
        }
    }
}
