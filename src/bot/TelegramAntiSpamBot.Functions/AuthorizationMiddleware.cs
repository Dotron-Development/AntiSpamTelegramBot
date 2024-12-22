namespace TelegramAntiSpamBot.Functions
{
    internal class AuthorizationMiddleware(
        IOptions<TelegramBotConfiguration> options, 
        ILogger<AuthorizationMiddleware> logger) : IFunctionsWorkerMiddleware
    {
        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var requestData = await context.GetHttpRequestDataAsync();
            if (requestData!.Headers.TryGetValues("X-Telegram-Bot-Api-Secret", out var secrets)
                && secrets.Any(x => x.Equals(options.Value.SecretHeader)))
            {
                await next(context);
            }
            else
            {
                var resp = requestData.CreateResponse();
                resp.StatusCode = HttpStatusCode.Forbidden;
                context.GetInvocationResult().Value = resp;

                logger.LogWarning("Unauthorized access attempt");
            }
        }
    }
}
