namespace TelegramAntiSpamBot.Functions;

public sealed class SnapshotReviewFunctions(PostSnapshotBuilder snapshots, SnapshotResetQueue queue, ITelegramBotClient bot)
{
    private static readonly JsonObjectSerializer Serializer = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Function(nameof(ListSearchSnapshots))]
    public async Task<HttpResponseData> ListSearchSnapshots(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "review/search-snapshots")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var items = new List<SnapshotResponse>();
        if (request.Query["channelId"] is { } value)
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var channelId) || channelId >= 0)
            {
                var error = request.CreateResponse(HttpStatusCode.BadRequest);
                await error.WriteAsJsonAsync(new { error = "A negative channel ID is required." }, Serializer, cancellationToken);
                return error;
            }
            if (await snapshots.FindAsync(channelId, cancellationToken) is { } blob)
                items.Add(new SnapshotResponse(blob.Name, blob.SizeBytes, blob.LastModified));
        }
        else
        {
            await foreach (var blob in snapshots.ListAsync(cancellationToken))
                items.Add(new SnapshotResponse(blob.Name, blob.SizeBytes, blob.LastModified));
        }
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(items, Serializer, cancellationToken);
        return response;
    }

    [Function(nameof(ResolveSearchSnapshotChannel))]
    public async Task<HttpResponseData> ResolveSearchSnapshotChannel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "review/search-snapshots/groups/{groupId:long}/channel")] HttpRequestData request,
        long groupId, CancellationToken cancellationToken)
    {
        if (groupId >= 0)
            return await Error(HttpStatusCode.BadRequest, "A negative discussion group ID is required.");

        ChatFullInfo chat;
        try
        {
            chat = await bot.GetChat(groupId, cancellationToken);
        }
        catch (ApiRequestException)
        {
            return await Error(HttpStatusCode.BadGateway, "Telegram could not look up this chat. Check that the bot can access it and try again.");
        }

        if (chat.Type is not (ChatType.Group or ChatType.Supergroup) || chat.LinkedChatId is not (< 0 and var channelId))
            return await Error(HttpStatusCode.UnprocessableEntity, "This chat is not a discussion group with a linked channel.");

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { channelId = channelId.ToString(CultureInfo.InvariantCulture) }, Serializer, cancellationToken);
        return response;

        async Task<HttpResponseData> Error(HttpStatusCode status, string message)
        {
            var error = request.CreateResponse(status);
            await error.WriteAsJsonAsync(new { error = message }, Serializer, cancellationToken);
            return error;
        }
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
