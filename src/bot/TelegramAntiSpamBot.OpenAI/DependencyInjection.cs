namespace TelegramAntiSpamBot.OpenAI
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddOpenAiService(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient(provider =>
            {
                var client = new AzureOpenAIClient(
                    new Uri("https://open-ai-telegram-assistant.openai.azure.com/"), 
                    new DefaultAzureCredential());
                var chatClient = client.GetChatClient("gpt-4o-mini");
                return chatClient;
            });

            serviceCollection.AddSingleton<SpamDetectionInstructions>();
            serviceCollection.AddScoped<ISpamDetectionService, SpamDetectionService>();
            return serviceCollection;
        }
    }
}
