#pragma warning disable OPENAI001

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

            serviceCollection.AddKeyedScoped("Mention", (provider, _) =>
                CreateMentionClient(provider.GetRequiredService<IOptions<OpenAiServicesConfiguration>>().Value));

            serviceCollection.AddSingleton(provider =>
            {
                var config = provider.GetRequiredService<IOptions<OpenAiServicesConfiguration>>().Value;
                var options = new AzureOpenAIClientOptions(AzureOpenAIClientOptions.ServiceVersion.V2024_10_21);
                return CreateAzureClient(config, options).GetEmbeddingClient(config.SearchEmbeddingDeployment);
            });

            serviceCollection.AddSingleton<SpamDetectionInstructions>();
            serviceCollection.AddTransient<MentionHistoryToolFactory>();
            serviceCollection.AddScoped<ISpamDetectionService, SpamDetectionService>();
            serviceCollection.AddScoped<IMentionService, MentionService>();
            return serviceCollection;
        }

        internal static ResponsesClientOptions CreateMentionClientOptions(Uri endpoint) => new()
        {
            Endpoint = endpoint,
            RetryPolicy = new ClientRetryPolicy(5)
        };

        internal static ResponsesClient CreateMentionClient(OpenAiServicesConfiguration config)
        {
            var endpoint = new Uri(new Uri(config.ServiceUrl.TrimEnd('/') + "/"), "openai/v1/");
            var options = CreateMentionClientOptions(endpoint);
            if (!string.IsNullOrEmpty(config.ApiKey))
                return new ResponsesClient(new ApiKeyCredential(config.ApiKey), options);

            var credential = config.OpenAiIdentityClientId is { } clientId
                ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId })
                : new DefaultAzureCredential();
            return new ResponsesClient(new BearerTokenPolicy(credential, "https://ai.azure.com/.default"), options);
        }

        private static ChatClient CreateChatClient(IServiceProvider provider, Func<OpenAiServicesConfiguration, string> deploymentSelector,
            AzureOpenAIClientOptions? options = null)
        {
            var config = provider.GetRequiredService<IOptions<OpenAiServicesConfiguration>>().Value;
            return CreateAzureClient(config, options).GetChatClient(deploymentSelector(config));
        }

        private static AzureOpenAIClient CreateAzureClient(OpenAiServicesConfiguration config, AzureOpenAIClientOptions? options)
        {
            var endpoint = new Uri(config.ServiceUrl);
            if (!string.IsNullOrEmpty(config.ApiKey))
                return new AzureOpenAIClient(endpoint, new ApiKeyCredential(config.ApiKey), options);

            var credential = config.OpenAiIdentityClientId is { } clientId
                ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId })
                : new DefaultAzureCredential();
            return new AzureOpenAIClient(endpoint, credential, options);
        }
    }
}
