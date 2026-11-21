namespace TelegramAntiSpamBot.Commands;

internal static partial class SlashCommandSyntax
{
    internal static int MatchLength(string message, string command, string botName, bool acceptsArguments = false)
    {
        var match = CommandPattern().Match(message);
        if (!match.Success || (!acceptsArguments && match.Length != message.Length) ||
            !match.Groups["command"].ValueSpan.Equals(command, StringComparison.OrdinalIgnoreCase))
            return 0;

        var username = match.Groups["username"];
        return !username.Success || IsInlineCommand(username, botName) ? match.Length : 0;
    }

    // This is the /command@BotName form, not Telegram's InlineQuery mode.
    private static bool IsInlineCommand(Group username, string botName) =>
        username.ValueSpan.Equals(botName.AsSpan().TrimStart('@'), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\A(?<command>/[^@\s]+)(?:@(?<username>[^@\s]+))?(?=\s|\z)")]
    private static partial Regex CommandPattern();
}
