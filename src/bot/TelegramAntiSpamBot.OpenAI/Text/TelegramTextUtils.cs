namespace TelegramAntiSpamBot.OpenAI;

internal static class TelegramTextUtils
{
    public static string Truncate(string text)
    {
        // Leave room below Telegram's 4096-character limit.
        const int limit = 4000;
        if (text.Length <= limit)
            return text;

        var length = limit - 3;
        if (char.IsHighSurrogate(text[length - 1]))
            length--;
        return text[..length] + "...";
    }
}
