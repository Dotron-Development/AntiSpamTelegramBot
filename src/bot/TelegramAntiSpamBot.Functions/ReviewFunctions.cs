namespace TelegramAntiSpamBot.Functions;

public sealed class ReviewFunctions(IAntiSpamBotRepository repository, ITelegramBotClient bot)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Function("GetReviewChats")]
    public async Task<HttpResponseData> GetChats(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "review/chats")] HttpRequestData request)
    {
        var pageSize = 100;
        if (request.Query["pageSize"] is { } size &&
            (!int.TryParse(size, out pageSize) || pageSize is < 1 or > 1000))
            return await Error(request, HttpStatusCode.BadRequest, "pageSize must be between 1 and 1000.");

        var page = await repository.GetReviewChatsAsync(pageSize, request.Query["continuationToken"]);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(page);
        return response;
    }

    [Function("GetReviewMessages")]
    public async Task<HttpResponseData> GetMessages(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "review/messages")] HttpRequestData request)
    {
        if (!long.TryParse(request.Query["chatId"], out var chatId) || chatId == 0)
            return await Error(request, HttpStatusCode.BadRequest, "A non-zero chatId is required.");

        var pageSize = 100;
        if (request.Query["pageSize"] is { } size &&
            (!int.TryParse(size, out pageSize) || pageSize is < 1 or > 1000))
            return await Error(request, HttpStatusCode.BadRequest, "pageSize must be between 1 and 1000.");

        var page = await repository.GetReviewMessagesAsync(chatId, pageSize, request.Query["continuationToken"]);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(page);
        return response;
    }

    [Function("ReviewMessage")]
    public async Task<HttpResponseData> Review(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "review/messages/{chatId:long}/{messageId:int}")]
        HttpRequestData request, long chatId, int messageId)
    {
        if (chatId == 0 || messageId <= 0)
            return await Error(request, HttpStatusCode.BadRequest, "Valid chatId and messageId are required.");

        ReviewMessageRequest? decision;
        try
        {
            decision = await JsonSerializer.DeserializeAsync<ReviewMessageRequest>(request.Body, JsonOptions);
        }
        catch (JsonException)
        {
            return await Error(request, HttpStatusCode.BadRequest, "Expected JSON with a boolean isSpam field.");
        }

        if (decision?.IsSpam is not { } isSpam)
            return await Error(request, HttpStatusCode.BadRequest, "A boolean isSpam field is required.");

        var message = await repository.GetReviewMessageAsync(chatId, messageId);
        if (message is null)
            return await Error(request, HttpStatusCode.NotFound, "Message not found in the review queue.");

        if (!string.IsNullOrEmpty(message.ContentHash))
            await repository.SaveMessageHash(message.ContentHash, isSpam);

        if (!isSpam)
        {
            var content = string.Join("\n", new[] { message.Message, message.ImageText }
                .Where(text => !string.IsNullOrEmpty(text)));
            if (!string.IsNullOrEmpty(content))
            {
                var text = $"from: {message.UserName}\n{content}";
                var offset = 0;
                while (offset < text.Length)
                {
                    var length = Math.Min(4096, text.Length - offset);
                    if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1]))
                        length--;
                    await bot.SendMessage(chatId, text.Substring(offset, length));
                    offset += length;
                }
            }
        }

        await repository.DeleteReviewMessageAsync(chatId, messageId);
        return request.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("RemoveRestrictions")]
    public async Task<HttpResponseData> RemoveRestrictions(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "review/restrictions/{chatId:long}/{userId:long}")]
        HttpRequestData request, long chatId, long userId)
    {
        if (chatId == 0 || userId <= 0)
            return await Error(request, HttpStatusCode.BadRequest, "Valid chatId and userId are required.");

        var chat = await bot.GetChat(chatId);
        if (chat.Permissions is null)
            return await Error(request, HttpStatusCode.Conflict, "Default chat permissions are unavailable.");

        await bot.RestrictChatMember(chatId, userId, chat.Permissions, useIndependentChatPermissions: true);
        return request.CreateResponse(HttpStatusCode.NoContent);
    }

    private static async Task<HttpResponseData> Error(HttpRequestData request, HttpStatusCode status, string message)
    {
        var response = request.CreateResponse(status);
        await response.WriteAsJsonAsync(new { error = message });
        response.StatusCode = status;
        return response;
    }
}

/// <summary>Specifies the human spam classification for a queued message.</summary>
public sealed record ReviewMessageRequest(bool? IsSpam);