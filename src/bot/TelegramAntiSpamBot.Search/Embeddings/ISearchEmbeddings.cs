namespace TelegramAntiSpamBot.Search;

public interface ISearchEmbeddings
{
    Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);
}
