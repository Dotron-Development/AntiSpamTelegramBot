namespace TelegramAntiSpamBot.Search;

internal sealed class AzureSearchEmbeddings(EmbeddingClient client) : ISearchEmbeddings
{
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0) return [];
        if (inputs.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Embedding inputs must not be empty.", nameof(inputs));

        var result = await client.GenerateEmbeddingsAsync(inputs, cancellationToken: cancellationToken);
        var vectors = result.Value.Select(embedding => embedding.ToFloats()).ToArray();
        if (vectors.Length != inputs.Count || vectors.Any(vector => !VectorFormat.IsValid(vector.Span)))
            throw new InvalidDataException("Foundry returned an incomplete or invalid embedding batch.");
        return vectors;
    }
}
