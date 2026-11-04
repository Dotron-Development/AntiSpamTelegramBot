namespace TelegramAntiSpamBot.Functions;

public sealed class MentionReviewFunctions(
    IMentionService mentionService,
    ILogger<MentionReviewFunctions> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonObjectSerializer Serializer = new(JsonOptions);

    /// <summary>Generates a UI-only mention preview without Telegram, history, or cooldown side effects.</summary>
    [Function("PreviewMention")]
    public async Task<HttpResponseData> PreviewMention(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "review/mention")] HttpRequestData request,
        CancellationToken cancellationToken = default)
    {
        PreviewRequest? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<PreviewRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return await Error(request, HttpStatusCode.BadRequest, "Expected a valid mention preview JSON request.", cancellationToken);
        }

        if (input?.Question is null || input.Question.Length > 4096 ||
            !ValidAuthor(input.FromName, input.FromUsername) ||
            (input.ReplyTo is { } reply &&
                (reply.Text is null || reply.Text.Length > 4096 || !ValidAuthor(reply.Name, reply.Username))))
            return await Error(request, HttpStatusCode.BadRequest,
                "question and replyTo.text must be strings of at most 4096 characters; names at most 200 and usernames at most 64.",
                cancellationToken);

        var mentionRequest = new MentionRequest(input.Question, Author(input.FromName, input.FromUsername),
            input.ReplyTo is { } replyTo
                ? new HistoryMessageReply(0, Author(replyTo.Name, replyTo.Username), replyTo.Text!)
                : null);
        string answer;
        try
        {
            answer = await mentionService.GenerateAsync(mentionRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to generate mention preview");
            return await Error(request, HttpStatusCode.BadGateway, "Unable to generate mention preview. Please try again later.", cancellationToken);
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { answer }, Serializer, cancellationToken);
        return response;
    }

    private static bool ValidAuthor(string? name, string? username) =>
        (name is null || name.Length <= 200) && (username is null || username.Length <= 64);

    private static HistoryMessageAuthor? Author(string? name, string? username) =>
        name is null && username is null ? null : new HistoryMessageAuthor(0, name ?? string.Empty, username);

    private static async Task<HttpResponseData> Error(
        HttpRequestData request, HttpStatusCode status, string error, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(status);
        await response.WriteAsJsonAsync(new { error }, Serializer, cancellationToken);
        response.StatusCode = status;
        return response;
    }

    private sealed record PreviewRequest(string? Question, string? FromName, string? FromUsername, PreviewReply? ReplyTo);
    private sealed record PreviewReply(string? Name, string? Username, string? Text);
}
