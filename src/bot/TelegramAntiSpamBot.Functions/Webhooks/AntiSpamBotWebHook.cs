namespace TelegramAntiSpamBot.Functions
{
    public class AntiSpamBotWebHook(ILogger<AntiSpamBotWebHook> logger,
        IChatsRepository chatsRepository,
        SpamMessageProcessor messageProcessor,
        ICommandProcessor commandProcessor,
        IOptions<TelegramBotConfiguration> botConfig,
        PostIndexQueue indexQueue)
    {
        [Function("AntiSpamBotWebHook")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            try
            {
                var update = JsonSerializer.Deserialize<Update>(req.Body, JsonBotAPI.Options);

                if (update is null || ExtractMessageFromUpdate(update) is not { } message ||
                    message.Chat.Id == botConfig.Value.ForwardSpamToChatId)
                    return req.CreateResponse(HttpStatusCode.NoContent);

                try
                {
                    if (TelegramMessageSource.IsServiceOrAutomaticForward(message))
                    {
                        if (TelegramPostIndexJob.FromUpdate(update) is { } job)
                            await indexQueue.EnqueueAsync(job, req.FunctionContext.CancellationToken);
                    }
                    else if (commandProcessor.IsCommand(update.Message?.Text))
                        await commandProcessor.ProcessCommand(update);
                    else
                        await messageProcessor.ProcessMessageAsync(message, update.Type, update.Id);
                }
                finally
                {
                    await RegisterChatAsync(message.Chat);
                }

                return req.CreateResponse(HttpStatusCode.NoContent);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unable to process Telegram update");
                throw;
            }
        }

        private async Task RegisterChatAsync(Chat chat)
        {
            try
            {
                var name = chat.Title ?? string.Join(" ", new[] { chat.FirstName, chat.LastName }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));
                if (string.IsNullOrWhiteSpace(name))
                    name = chat.Username ?? chat.Id.ToString(CultureInfo.InvariantCulture);
                await chatsRepository.SaveChatAsync(new ReviewChat(chat.Id, name, DateTimeOffset.UtcNow));
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Unable to update chat catalog for chat {chatId}", chat.Id);
            }
        }

        private static Message? ExtractMessageFromUpdate(Update update)
        {
            return update switch
            {
                { Type: UpdateType.Message, Message: { } addedMessage } => addedMessage,
                { Type: UpdateType.EditedMessage, EditedMessage: { } editedMessage } => editedMessage,
                { Type: UpdateType.ChannelPost, ChannelPost: { } channelPost } => channelPost,
                { Type: UpdateType.EditedChannelPost, EditedChannelPost: { } editedChannelPost } => editedChannelPost,
                _ => null
            };
        }
    }
}
