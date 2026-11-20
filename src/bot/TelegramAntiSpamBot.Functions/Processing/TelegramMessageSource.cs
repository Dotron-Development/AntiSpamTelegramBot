namespace TelegramAntiSpamBot.Functions;

internal static class TelegramMessageSource
{
    private const long TelegramServiceUserId = 777000;

    public static bool IsServiceOrAutomaticForward(Message message) =>
        message.IsAutomaticForward || message.From?.Id == TelegramServiceUserId;
}
