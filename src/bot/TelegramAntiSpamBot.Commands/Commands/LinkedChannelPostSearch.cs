namespace TelegramAntiSpamBot.Commands;

internal sealed class LinkedChannelPostSearch(ITelegramBotClient bot, PostSearch search) : IChannelPostSearch
{
    public async Task<ChannelPostSearchResult> FindAsync(long chatId, string query, CancellationToken cancellationToken)
    {
        var chat = await bot.GetChat(chatId, cancellationToken);
        if (chat.Type is not (ChatType.Group or ChatType.Supergroup) || chat.LinkedChatId is not (< -100 and var channelId))
            return new(ChannelPostSearchStatus.NoLinkedChannel, []);

        var channel = channelId.ToString(CultureInfo.InvariantCulture);
        if (!channel.StartsWith("-100", StringComparison.Ordinal))
            return new(ChannelPostSearchStatus.NoLinkedChannel, []);

        var hits = await search.FindAsync(channelId, query, cancellationToken: cancellationToken);
        if (hits is null)
            return new(ChannelPostSearchStatus.IndexNotReady, []);

        return new(ChannelPostSearchStatus.Completed, hits.Select(hit =>
        {
            var title = string.Join(' ', hit.Title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (title.Length > 160)
                title = title[..(char.IsHighSurrogate(title[159]) ? 159 : 160)] + "…";
            var id = hit.MessageId.ToString(CultureInfo.InvariantCulture);
            return new ChannelPostLink(title, $"https://t.me/c/{channel[4..]}/{id}");
        }).ToArray());
    }
}
