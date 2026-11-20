namespace TelegramAntiSpamBot.Functions;

public sealed class ReviewAccessFunctions
{
    [Function(nameof(CheckTelegramReviewAccess))]
    public HttpResponseData CheckTelegramReviewAccess(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "review/access")] HttpRequestData request) =>
        request.CreateResponse(HttpStatusCode.NoContent);
}
