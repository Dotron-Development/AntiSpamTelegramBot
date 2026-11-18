namespace TelegramAntiSpamBot.IntegrationTests;

internal sealed class SearchEmbeddingsStub : ISearchEmbeddings
{
    public List<string> Inputs { get; } = [];
    public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        Inputs.AddRange(inputs);
        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(inputs.Select(_ => ReadOnlyMemory<float>.Empty).ToArray());
    }
}
