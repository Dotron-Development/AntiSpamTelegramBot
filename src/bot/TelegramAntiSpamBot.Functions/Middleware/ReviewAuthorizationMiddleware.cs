namespace TelegramAntiSpamBot.Functions;

internal sealed class ReviewAuthorizationMiddleware(TelegramReviewAuth auth) : IFunctionsWorkerMiddleware
{
    private const string AuthorizationPrefix = "tma ";

    internal static bool RequiresTelegramAuthorization(string functionName, IEnumerable<string> inputBindingTypes) =>
        functionName != "AntiSpamBotWebHook" &&
        inputBindingTypes.Contains("httpTrigger", StringComparer.OrdinalIgnoreCase);

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var request = await context.GetHttpRequestDataAsync() ??
            throw new InvalidOperationException("Expected an HTTP request for the review API.");
        var authorization = request.Headers.TryGetValues("Authorization", out var values)
            ? values.Take(2).ToArray()
            : [];

        var status = CheckAuthorization(authorization);
        if (status is { } rejection)
        {
            var response = request.CreateResponse(rejection);
            if (rejection == HttpStatusCode.Unauthorized)
                response.Headers.Add("WWW-Authenticate", "tma");
            context.GetInvocationResult().Value = response;
            return;
        }

        await next(context);
        if (context.GetInvocationResult().Value is HttpResponseData result)
            result.Headers.Add("Cache-Control", "no-store");
    }

    internal HttpStatusCode? CheckAuthorization(string[] authorization)
    {
        if (authorization.Length != 1 ||
            !authorization[0].StartsWith(AuthorizationPrefix, StringComparison.OrdinalIgnoreCase) ||
            auth.GetVerifiedLaunchUser(authorization[0][AuthorizationPrefix.Length..]) is not { } user)
            return HttpStatusCode.Unauthorized;

        return auth.IsModerator(user.Id) ? null : HttpStatusCode.Forbidden;
    }
}
