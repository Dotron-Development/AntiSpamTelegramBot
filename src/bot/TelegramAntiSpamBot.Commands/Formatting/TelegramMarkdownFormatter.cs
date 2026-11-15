namespace TelegramAntiSpamBot.Commands;

internal static class TelegramMarkdownFormatter
{
    private const int Limit = 4096;
    private const string Reserved = "\\_*[]()~`>#+-=|{}.!";

    public static string Format(string text) => FormatCore(text, false).FirstOrDefault() ?? string.Empty;

    public static IReadOnlyList<string> FormatParts(string text) => FormatCore(text, true);

    private static IReadOnlyList<string> FormatCore(string text, bool split)
    {
        var parts = new List<string>();
        var result = new StringBuilder();
        bool Flush()
        {
            if (result.Length > 0)
                parts.Add(result.ToString());
            result.Clear();
            return split;
        }

        for (var offset = 0; offset < text.Length;)
        {
            if (TryFormatLink(text, offset, out var linkLength, out var link))
            {
                if (link.Length <= Limit)
                {
                    if (result.Length + link.Length > Limit && !Flush())
                        break;
                    result.Append(link);
                    offset += linkLength;
                    continue;
                }
                // An oversized link is displayed as escaped text, not a broken Telegram entity.
                AppendEscaped(result, text.AsSpan(offset, linkLength), false, Limit, out var consumed);
                offset += consumed;
                if (!Flush())
                    break;
                continue;
            }

            // Keep a clickable URL together when it fits in one message.
            if (split && (text.AsSpan(offset).StartsWith("https://") || text.AsSpan(offset).StartsWith("http://")))
            {
                var urlEnd = offset;
                while (urlEnd < text.Length && !char.IsWhiteSpace(text[urlEnd]))
                    urlEnd++;
                var url = text.AsSpan(offset, urlEnd - offset);
                var escapedLength = url.Length;
                foreach (var c in url)
                    if (Reserved.Contains(c))
                        escapedLength++;
                if (escapedLength <= Limit)
                {
                    if (result.Length + escapedLength > Limit)
                        Flush();
                    AppendEscaped(result, url, false, Limit, out _);
                    offset = urlEnd;
                    continue;
                }
            }

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

                if (prefix.Length + wrapper.Length + 2 > Limit)
                {
                    // An oversized language label cannot form a Telegram code entity.
                    marker = null;
                }
                else
                {
                    var consumed = 0;
                    while (consumed < content.Length)
                    {
                        if (Limit - result.Length <= prefix.Length + wrapper.Length)
                        {
                            if (!Flush())
                                return parts;
                            continue;
                        }
                        var start = result.Length;
                        result.Append(prefix);
                        var complete = AppendEscaped(result, content.AsSpan(consumed), code, Limit - wrapper.Length, out var appended);
                        if (appended == 0)
                            result.Length = start;
                        else
                            result.Append(wrapper);
                        consumed += appended;
                        if (!complete && !Flush())
                            return parts;
                    }
                    offset = end + marker.Length;
                    continue;
                }
            }

            var length = char.IsHighSurrogate(text[offset]) && offset + 1 < text.Length && char.IsLowSurrogate(text[offset + 1]) ? 2 : 1;
            if (!AppendEscaped(result, text.AsSpan(offset, length), false, Limit, out _))
            {
                if (!Flush())
                    break;
                continue;
            }
            offset += length;
        }
        if (result.Length > 0)
            parts.Add(result.ToString());
        return parts;
    }

    private static bool TryFormatLink(string text, int offset, out int length, out string formatted)
    {
        length = 0;
        formatted = string.Empty;
        if (text[offset] != '[')
            return false;
        var label = new StringBuilder();
        var end = offset + 1;
        while (end < text.Length && text[end] != ']')
        {
            if (text[end] == '\\' && end + 1 < text.Length &&
                text[end + 1] is >= '!' and <= '~' && !char.IsAsciiLetterOrDigit(text[end + 1]))
                end++;
            label.Append(text[end]);
            end++;
        }
        if (label.Length == 0 || !text.AsSpan(end).StartsWith("]("))
            return false;
        var urlStart = end + 2;
        if (!text.AsSpan(urlStart).StartsWith("https://") && !text.AsSpan(urlStart).StartsWith("http://"))
            return false;
        var url = new StringBuilder();
        var depth = 1;
        for (var index = urlStart; index < text.Length && !char.IsWhiteSpace(text[index]); index++)
        {
            var c = text[index];
            if (c == '\\' && index + 1 < text.Length && text[index + 1] is '\\' or '(' or ')')
                c = text[++index];
            else if (c == '(')
                depth++;
            else if (c == ')' && --depth == 0)
            {
                if (!Uri.TryCreate(url.ToString(), UriKind.Absolute, out var target) ||
                    target.Scheme is not ("http" or "https"))
                    return false;
                var result = new StringBuilder("[");
                AppendEscaped(result, label.ToString(), false, int.MaxValue, out _);
                result.Append("](");
                foreach (var character in url.ToString())
                {
                    if (character is ')' or '\\')
                        result.Append('\\');
                    result.Append(character);
                }
                result.Append(')');
                length = index - offset + 1;
                formatted = result.ToString();
                return true;
            }
            url.Append(c);
        }
        return false;
    }

    private static string? GetMarker(string text, int offset)
    {
        var remaining = text.AsSpan(offset);
        if (remaining.StartsWith("```")) return "```";
        if (remaining.StartsWith("**")) return "**";
        return text[offset] switch { '`' => "`", '_' => "_", '*' => "*", _ => null };
    }

    private static bool AppendEscaped(StringBuilder result, ReadOnlySpan<char> text, bool code, int limit, out int consumed)
    {
        consumed = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            var length = char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
            var escape = code ? c is '\\' or '`' : Reserved.Contains(c);
            if (result.Length + length + (escape ? 1 : 0) > limit)
                return false;
            if (escape)
                result.Append('\\');
            result.Append(text.Slice(index, length));
            index += length - 1;
            consumed = index + 1;
        }
        return true;
    }
}
