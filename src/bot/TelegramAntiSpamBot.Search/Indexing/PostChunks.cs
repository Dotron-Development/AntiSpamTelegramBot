namespace TelegramAntiSpamBot.Search;

internal static partial class PostChunks
{
    private const int MaxTokens = 500;
    private static readonly TiktokenTokenizer Tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");

    public static (string Title, IReadOnlyList<string> Inputs) Prepare(string text, string? authorTitle)
    {
        text = text.ReplaceLineEndings("\n").Trim();
        var firstBlock = ParagraphBreak().Split(text, 2)[0];
        var title = string.IsNullOrWhiteSpace(authorTitle)
            ? (firstBlock.Length <= 240 ? firstBlock : text.Split('\n', 2)[0])
            : authorTitle.ReplaceLineEndings("\n").Trim();
        if (string.IsNullOrWhiteSpace(authorTitle) && Tokenizer.CountTokens(title) > 120)
            title = title[..Boundary(title, 120)].TrimEnd() + "…";
        var prefix = Tokenizer.CountTokens(title) > 120 ? title[..Boundary(title, 120)] + "\n\n" : title + "\n\n";
        var body = text.StartsWith(title, StringComparison.Ordinal) ? text[title.Length..].Trim() : text;
        if (body.Length == 0) body = title;

        var inputs = new List<string>();
        var current = "";
        foreach (var block in ParagraphBreak().Split(body))
        {
            var remaining = block.Trim();
            if (remaining.Length == 0) continue;
            var combined = current.Length == 0 ? remaining : current + "\n\n" + remaining;
            if (Tokenizer.CountTokens(prefix + combined) <= MaxTokens)
            {
                current = combined;
                continue;
            }
            if (current.Length > 0) inputs.Add(prefix + current);
            while (Tokenizer.CountTokens(prefix + remaining) > MaxTokens)
            {
                var length = Boundary(remaining, MaxTokens - Tokenizer.CountTokens(prefix) - 4);
                var space = remaining.AsSpan(0, length).LastIndexOfAny(' ', '\n', '\t');
                if (space > length / 2) length = space;
                while (Tokenizer.CountTokens(prefix + remaining[..length].TrimEnd()) > MaxTokens)
                    length = Boundary(remaining, Tokenizer.CountTokens(remaining[..length]) - 1);
                inputs.Add(prefix + remaining[..length].TrimEnd());
                remaining = remaining[length..].TrimStart();
            }
            current = remaining;
        }
        if (current.Length > 0) inputs.Add(prefix + current);
        return (title, inputs);
    }

    private static int Boundary(string text, int tokens)
    {
        var index = Tokenizer.GetIndexByTokenCount(text, tokens, out _, out _);
        if (index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1])) index--;
        return index > 0 ? index : throw new InvalidDataException("Could not split post on a valid character boundary.");
    }

    [GeneratedRegex(@"\n[ \t]*\n+")]
    private static partial Regex ParagraphBreak();
}
