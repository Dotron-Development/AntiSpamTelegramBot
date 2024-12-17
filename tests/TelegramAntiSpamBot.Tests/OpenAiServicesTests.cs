namespace TelegramAntiSpamBot.Tests
{
    [TestClass]
    public sealed class OpenAiServicesTests
    {
        private SpamDetectionService service;

        public OpenAiServicesTests()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddOpenAiService();
            services.AddLogging();

            var sp = services.BuildServiceProvider();
            service = sp.GetRequiredService<SpamDetectionService>();
        }

        [DataTestMethod]
        [DataRow(SpamData.Spam1, 10)]
        [DataRow(SpamData.Spam2, 2)]
        [DataRow(SpamData.Spam3, 3)]
        [DataRow(SpamData.Spam4, 4)]
        [DataRow(SpamData.Spam5, 1)]
        public async Task OpenAI_Should_DetectSpam(string message, int messageCount)
        {
            var result = await service.IsSpam(message, messageCount);
            
            result.ResultType.Should().Be(ResultType.Evaluated);
            result.Probability.Should().BeGreaterThanOrEqualTo(90);
        }
    }
}
