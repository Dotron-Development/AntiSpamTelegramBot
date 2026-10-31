
namespace TelegramAntiSpamBot.Functions;

public sealed class HistoryReviewFunctions(
    IMessageHistoryRepository historyRepository,
    ICommandCallsRepository commandCallsRepository,
    IChronicleService chronicleService,
    IOptions<TelegramBotConfiguration> configuration,
    ILogger<HistoryReviewFunctions> logger)
{
    private static readonly JsonObjectSerializer Serializer = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));
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

    [Function("PreviewChronicle")]
    public async Task<HttpResponseData> PreviewChronicle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "review/chronicle/{chatId:long}")] HttpRequestData request,
        long chatId, CancellationToken cancellationToken = default)
    {
        if (chatId == 0)
            return await Error(request, HttpStatusCode.BadRequest, "A non-zero chatId is required.");
        try
        {
            var allowed = await commandCallsRepository.TryAcquireAsync(chatId, "/chronicle", DateTimeOffset.UtcNow,
                configuration.Value.CommandCooldown, cancellationToken);
            if (!allowed)
                logger.LogInformation("Skipping Chronicle preview for chat {chatId}: cooldown is active", chatId);
            var summary = allowed ? await chronicleService.GenerateAsync(chatId, cancellationToken: cancellationToken) : null;
            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new ChroniclePreviewResponse(chatId, summary, !allowed), Serializer, cancellationToken);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to generate Chronicle preview for chat {chatId}", chatId);
            return await Error(request, HttpStatusCode.BadGateway, "Unable to generate Chronicle. Please try again later.");
        }
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

/// <summary>A UI-only Chronicle preview; skipped means the configured command cooldown is active.</summary>
public sealed record ChroniclePreviewResponse(long ChatId, string? Summary, bool Skipped);