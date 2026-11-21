namespace TelegramAntiSpamBot.Search;

public interface IChannelPostSearch
{
    Task<ChannelPostSearchResult> FindAsync(long chatId, string query, CancellationToken cancellationToken);
}

public sealed record ChannelPostSearchResult(ChannelPostSearchStatus Status, IReadOnlyList<ChannelPostLink> Posts);

public sealed record ChannelPostLink(string Title, string Url);

public enum ChannelPostSearchStatus
{
    Completed,
    NoLinkedChannel,
    IndexNotReady
}
