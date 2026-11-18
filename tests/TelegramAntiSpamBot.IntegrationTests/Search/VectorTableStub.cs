namespace TelegramAntiSpamBot.IntegrationTests;

internal sealed class VectorTableStub : TableClient
{
    public long? Revision { get; init; }
    public string? Filter { get; private set; }
    public TableTransactionAction[] Actions { get; private set; } = [];

    public override AsyncPageable<T> QueryAsync<T>(string? filter = null, int? maxPerPage = null,
        IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
    {
        Filter = filter;
        var rows = new[] { "42:chunk:0000", "42:chunk:0001", "42:post" }
            .Select(key =>
            {
                var row = new TableEntity("-100", key) { ETag = new ETag("existing") };
                if (key.EndsWith(":post", StringComparison.Ordinal) && Revision is { } revision) row["Revision"] = revision;
                return (T)(object)row;
            }).ToArray();
        return AsyncPageable<T>.FromPages([Page<T>.FromValues(rows, null, null!)]);
    }

    public override Task<Response<IReadOnlyList<Response>>> SubmitTransactionAsync(
        IEnumerable<TableTransactionAction> transactionActions, CancellationToken cancellationToken = default)
    {
        Actions = transactionActions.ToArray();
        return Task.FromResult<Response<IReadOnlyList<Response>>>(null!);
    }
}
