namespace TelegramAntiSpamBot.Functions;

public sealed class SnapshotReviewFunctions(PostSnapshotBuilder snapshots, SnapshotResetQueue queue)
{
    private static readonly JsonObjectSerializer Serializer = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Function(nameof(ListSearchSnapshots))]
    public async Task<HttpResponseData> ListSearchSnapshots(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "review/search-snapshots")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var items = new List<SnapshotResponse>();
        await foreach (var blob in snapshots.ListAsync(cancellationToken))
            items.Add(new SnapshotResponse(blob.Name, blob.SizeBytes, blob.LastModified));
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(items, Serializer, cancellationToken);
        return response;
    }

    [Function(nameof(ResetSearchSnapshot))]
    public async Task<HttpResponseData> ResetSearchSnapshot(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "review/search-snapshots/{chatId:long}/reset")] HttpRequestData request,
        long chatId, CancellationToken cancellationToken)
    {
        if (chatId >= 0)
        {
            var error = request.CreateResponse(HttpStatusCode.BadRequest);
            await error.WriteAsJsonAsync(new { error = "A negative channel ID is required." }, Serializer, cancellationToken);
            return error;
        }
        await queue.EnqueueAsync(chatId, cancellationToken);
        return request.CreateResponse(HttpStatusCode.Accepted);
    }
}
