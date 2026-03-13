namespace TelegramAntiSpamBot.IntegrationTests
{
    public class SpamDetectorServiceTests
    {
        private readonly ServiceProvider _serviceProvider;

        public SpamDetectorServiceTests()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpenAiService();
            var builder = new ConfigurationBuilder()
                .AddUserSecrets<SpamDetectorServiceTests>()
                .AddJsonFile("appsettings.Test.json", optional: false);

            var configuration = builder.Build();
            services.AddSingleton<IConfiguration>(configuration);
            _serviceProvider = services.BuildServiceProvider();
        }


        [Fact]
        public async Task Test1()
        {
            var sut = _serviceProvider.GetRequiredService<ISpamDetectionService>();
            var result = await sut.IsSpamAsync("Стартует проект с выгодным доходом\r\nПредлагаем прибыль от 400$.\r\nДостаточно смартфона \r\nХочешь начать — отправляй ”+” @ann_mizkovva", 1, true);
        }
    }
}