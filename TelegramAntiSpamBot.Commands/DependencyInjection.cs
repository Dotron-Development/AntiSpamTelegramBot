using Microsoft.Extensions.Options;
using TelegramAntispamBot.Configuration;

namespace TelegramAntiSpamBot.Commands
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddCommands(this IServiceCollection serviceCollection)
        {
            var assembly = typeof(DependencyInjection).Assembly;
            var commandTypes = assembly
                .GetTypes()
                .Where(t => typeof(ICommand).IsAssignableFrom(t) &&
                            t is { IsInterface: false, IsAbstract: false });

            foreach (var type in commandTypes)
            {
                serviceCollection.AddTransient(typeof(ICommand), type);
            }

            serviceCollection.AddTransient<ICommandProcessor, CommandProcessor>(provider =>
            {
                var commands = provider.GetRequiredService<IEnumerable<ICommand>>();
                var config = provider.GetRequiredService<IOptions<TelegramBotConfiguration>>();
                return new CommandProcessor(commands, config.Value.BotName);
            });

            return serviceCollection;
        }
    }
}
