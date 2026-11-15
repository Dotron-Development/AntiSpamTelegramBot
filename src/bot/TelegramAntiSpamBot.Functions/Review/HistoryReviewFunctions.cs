
namespace TelegramAntiSpamBot.Functions;

public sealed class HistoryReviewFunctions(
    IMessageHistoryRepository historyRepository,
    ICommandCallsRepository commandCallsRepository)
{
    private static readonly JsonObjectSerializer Serializer = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Function("ClearChatHistory")]
    public async Task<HttpResponseData> ClearHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "review/history/{chatId:long}")] HttpRequestData request,
        long chatId, CancellationToken cancellationToken = default)
    {
        if (chatId == 0)
            return await Error(request, HttpStatusCode.BadRequest, "A non-zero chatId is required.");

        await historyRepository.ClearChatAsync(chatId, cancellationToken);
        return request.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("GetReviewHistory")]
    public async Task<HttpResponseData> GetHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "review/history")] HttpRequestData request,
        CancellationToken cancellationToken = default)
    {
        var query = ReadQuery(request);
        if (query.Error is not null)
            return await Error(request, HttpStatusCode.BadRequest, query.Error);
        var page = await historyRepository.GetPageAsync(query.ChatId, query.PageSize,
            request.Query["continuationToken"], cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(page, Serializer, cancellationToken);
        return response;
    }

    [Function("GetReviewCommandCalls")]
    public async Task<HttpResponseData> GetCommandCalls(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "review/command-calls")] HttpRequestData request,
        CancellationToken cancellationToken = default)
    {
        var query = ReadQuery(request);
        if (query.Error is not null)
            return await Error(request, HttpStatusCode.BadRequest, query.Error);
        var page = await commandCallsRepository.GetPageAsync(query.ChatId, query.PageSize,
            request.Query["continuationToken"], cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(page, Serializer, cancellationToken);
        return response;
    }

    private static (long? ChatId, int PageSize, string? Error) ReadQuery(HttpRequestData request)
    {
        long? chatId = null;
        if (request.Query["chatId"] is { } value)
        {
            if (!long.TryParse(value, out var parsed) || parsed == 0)
                return (null, 100, "chatId must be a non-zero integer when provided.");
            chatId = parsed;
        }
        var pageSize = 100;
        if (request.Query["pageSize"] is { } size &&
            (!int.TryParse(size, out pageSize) || pageSize is < 1 or > 1000))
            return (chatId, 100, "pageSize must be between 1 and 1000.");
        return (chatId, pageSize, null);
    }

    private static async Task<HttpResponseData> Error(HttpRequestData request, HttpStatusCode status, string error)
    {
        var response = request.CreateResponse(status);
        await response.WriteAsJsonAsync(new { error });
        response.StatusCode = status;
        return response;
    }
}
