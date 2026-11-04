namespace TelegramAntiSpamBot.Commands;

internal static class TelegramMarkdownFormatter
{
    private const int Limit = 4096;
    private const string Reserved = "\\_*[]()~`>#+-=|{}.!";

    public static string Format(string text)
    {
        var result = new StringBuilder();
        for (var offset = 0; offset < text.Length;)
        {
            var marker = GetMarker(text, offset);
            var end = marker is null ? -1 : text.IndexOf(marker, offset + marker.Length, StringComparison.Ordinal);
            if (marker is not null && end > offset + marker.Length)
            {
                var content = text[(offset + marker.Length)..end];
                var wrapper = marker switch { "**" => "*", "*" => "_", _ => marker };
                var code = marker[0] == '`';
                var prefix = wrapper;
                if (marker == "```")
                {
                    var newline = content.IndexOf('\n');
                    if (newline >= 0 && content[..newline].TrimEnd('\r').All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '+' or '-'))
                    {
                        prefix += content[..newline].TrimEnd('\r') + "\n";
                        content = content[(newline + 1)..];
                    }
                    else
                        prefix += "\n";
                }

                // Reserve closing markers before escaping/truncating a formatted span.
                if (Limit - result.Length <= prefix.Length + wrapper.Length)
                    break;
                var start = result.Length;
                result.Append(prefix);
                var complete = AppendEscaped(result, content, code, Limit - wrapper.Length);
                if (result.Length == start + prefix.Length)
                {
                    result.Length = start;
                    break;
                }
                result.Append(wrapper);
                if (!complete)
                    break;
                offset = end + marker.Length;
            }
            else
            {
                var length = char.IsHighSurrogate(text[offset]) && offset + 1 < text.Length && char.IsLowSurrogate(text[offset + 1]) ? 2 : 1;
                if (!AppendEscaped(result, text.Substring(offset, length), false, Limit))
                    break;
                offset += length;
            }
        }
        return result.ToString();
    }

    private static string? GetMarker(string text, int offset)
    {
        var remaining = text.AsSpan(offset);
        if (remaining.StartsWith("```")) return "```";
        if (remaining.StartsWith("**")) return "**";
        return text[offset] switch { '`' => "`", '_' => "_", '*' => "*", _ => null };
    }

    private static bool AppendEscaped(StringBuilder result, string text, bool code, int limit)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            var length = char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
            var escape = code ? c is '\\' or '`' : Reserved.Contains(c);
            if (result.Length + length + (escape ? 1 : 0) > limit)
                return false;
            if (escape)
                result.Append('\\');
            result.Append(text, index, length);
            index += length - 1;
        }
        return true;
    }
}
