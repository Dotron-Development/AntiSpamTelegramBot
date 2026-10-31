namespace TelegramAntiSpamBot.Commands
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddCommands(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddOptions<TelegramBotConfiguration>()
                .Validate(config => config.CommandCooldown >= TimeSpan.Zero, "CommandCooldown must not be negative.");
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
                var bot = provider.GetRequiredService<ITelegramBotClient>();
                var calls = provider.GetRequiredService<Persistence.Repositories.ICommandCallsRepository>();
                var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CommandProcessor>>();
                return new CommandProcessor(commands, config.Value, bot, calls, logger);
            });

            return serviceCollection;
        }
    }
}
