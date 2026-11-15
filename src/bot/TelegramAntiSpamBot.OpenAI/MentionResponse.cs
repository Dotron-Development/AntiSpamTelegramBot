namespace TelegramAntiSpamBot.OpenAI;

public sealed record MentionResponse(string Answer, IReadOnlyList<string> Sources);
