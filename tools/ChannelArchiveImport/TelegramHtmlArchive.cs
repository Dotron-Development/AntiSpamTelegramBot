using System.Globalization;
using System.Text;
using HtmlAgilityPack;
using TelegramAntiSpamBot.Search;

internal static class TelegramHtmlArchive
{
    public static IEnumerable<(long Id, PostIndexJob? Job)> Read(string directory, long channelId)
    {
        var pages = Directory.GetFiles(directory, "messages*.html")
            .Where(path => PageNumber(path) > 0)
            .OrderBy(PageNumber)
            .ToArray();
        if (pages.Length == 0)
            throw new InvalidDataException("No Telegram messages*.html pages found.");

        foreach (var page in pages)
        {
            var document = new HtmlDocument();
            using var reader = new StreamReader(page, Encoding.UTF8);
            document.Load(reader);
            foreach (var message in document.DocumentNode.Descendants("div").Where(node => HasClass(node, "message") && HasClass(node, "default")))
            {
                var id = message.Id.AsSpan();
                if (!id.StartsWith("message", StringComparison.Ordinal) ||
                    !long.TryParse(id[7..], NumberStyles.None, CultureInfo.InvariantCulture, out var messageId) || messageId <= 0)
                    throw new InvalidDataException($"Invalid post ID in {Path.GetFileName(page)}.");

                var body = message.ChildNodes.FirstOrDefault(node => HasClass(node, "body"));
                var content = body?.ChildNodes.FirstOrDefault(node => HasClass(node, "text"));
                yield return (messageId, content is null ? null : Extract(content, channelId, messageId));
            }
        }
    }

    private static PostIndexJob? Extract(HtmlNode content, long channelId, long messageId)
    {
        var text = new StringBuilder();
        var titleEnd = 0;
        var titleClosed = false;
        Walk(content, false);
        var raw = text.ToString();
        var clean = raw.Trim();
        if (clean.Length == 0) return null;

        var start = raw.Length - raw.TrimStart().Length;
        var titleLength = titleEnd > start ? raw.AsSpan(start, titleEnd - start).TrimEnd().Length : 0;
        // Archive jobs rank below any webhook revision, so they never overwrite live posts.
        return new PostIndexJob(channelId, messageId, 1, clean, TitleLength: titleLength);

        void Walk(HtmlNode node, bool bold)
        {
            if (node is HtmlTextNode value)
            {
                var decoded = HtmlEntity.DeEntitize(value.Text);
                text.Append(decoded);
                if (!titleClosed && !string.IsNullOrWhiteSpace(decoded))
                {
                    if (bold) titleEnd = text.Length;
                    else titleClosed = true;
                }
                return;
            }

            if (node.Name == "br") text.Append('\n');
            if (node.Name is "script" or "style") return;
            var block = node.Name is "pre" or "blockquote" or "p";
            if (block && text.Length > 0 && text[^1] != '\n') text.Append('\n');
            foreach (var child in node.ChildNodes) Walk(child, bold || node.Name is "strong" or "b");
            if (block && text.Length > 0 && text[^1] != '\n') text.Append('\n');
        }
    }

    private static bool HasClass(HtmlNode node, string name) =>
        node.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name);

    private static int PageNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name == "messages") return 1;
        return int.TryParse(name.AsSpan(8), NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page > 1 ? page : 0;
    }
}
