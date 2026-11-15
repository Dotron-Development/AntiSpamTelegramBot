using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TelegramAntispamBot.Configuration;
using TelegramAntiSpamBot.Commands;

namespace TelegramAntiSpamBot.IntegrationTests;

public class ConfigurationValidationTests
{
    [Theory]
    [InlineData(nameof(TelegramBotConfiguration.CommandCooldown), -1L, false)]
    [InlineData(nameof(TelegramBotConfiguration.CommandCooldown), 0L, true)]
    [InlineData(nameof(TelegramBotConfiguration.CommandCooldown), 1L, true)]
    [InlineData(nameof(TelegramBotConfiguration.CommandCooldown), 864000000000L, true)]
    [InlineData(nameof(TelegramBotConfiguration.CommandCooldown), 864000000001L, false)]
    [InlineData(nameof(TelegramBotConfiguration.CommandCooldown), long.MaxValue, false)]
    [InlineData(nameof(TelegramBotConfiguration.SpamRestrictionTimeout), -1L, false)]
    [InlineData(nameof(TelegramBotConfiguration.SpamRestrictionTimeout), 0L, true)]
    [InlineData(nameof(TelegramBotConfiguration.SpamRestrictionTimeout), 1L, true)]
    [InlineData(nameof(TelegramBotConfiguration.SpamRestrictionTimeout), 315360000000000L, true)]
    [InlineData(nameof(TelegramBotConfiguration.SpamRestrictionTimeout), 315360000000001L, false)]
    [InlineData(nameof(TelegramBotConfiguration.SpamRestrictionTimeout), long.MaxValue, false)]
    public void ConfigurationDurationsValidateThroughAttributes(string property, long ticks, bool valid)
    {
        var services = new ServiceCollection();
        services.AddCommands();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Token"] = "token",
            ["SecretHeader"] = "secret",
            ["BotName"] = "bot",
            [property] = TimeSpan.FromTicks(ticks).ToString("c", CultureInfo.InvariantCulture)
        }).Build();
        services.AddOptions<TelegramBotConfiguration>().Bind(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<TelegramBotConfiguration>>();

        if (valid)
        {
            var duration = property == nameof(TelegramBotConfiguration.CommandCooldown)
                ? options.Value.CommandCooldown
                : options.Value.SpamRestrictionTimeout;
            Assert.Equal(TimeSpan.FromTicks(ticks), duration);
        }
        else
        {
            var exception = Assert.Throws<OptionsValidationException>(() => options.Value);
            Assert.Contains(exception.Failures, failure => failure.Contains(property));
        }
    }
}
