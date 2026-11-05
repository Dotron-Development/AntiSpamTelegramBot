namespace TelegramAntiSpamBot.Functions
{
    public class AntiSpamBotWebHook(ILogger<AntiSpamBotWebHook> logger,
        IChatsRepository chatsRepository,
        SpamMessageProcessor messageProcessor,
        ICommandProcessor commandProcessor,
        IOptions<TelegramBotConfiguration> botConfig)
    {
        [Function("AntiSpamBotWebHook")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            try
            {
                var update = JsonSerializer.Deserialize<Update>(req.Body, JsonBotAPI.Options);

                var message = ExtractMessageFromUpdate(update);
                if (message is null)
                    return req.CreateResponse(HttpStatusCode.NoContent);

                var registerChatTask = RegisterChatAsync(update);

                try
                {
                    if (update is not null && commandProcessor.IsCommand(update.Message?.Text))
                        await commandProcessor.ProcessCommand(update);
                    else
                        await messageProcessor.ProcessMessageAsync(message, update!.Type, update.Id);
                }
                finally
                {
                    await registerChatTask;
                }

                return req.CreateResponse(HttpStatusCode.NoContent);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unable to process Telegram update");
                throw;
            }
        }

        private async Task RegisterChatAsync(Update? update)
        {
            var chat = ExtractMessageFromUpdate(update)?.Chat;
            if (chat is null || chat.Id == botConfig.Value.ForwardSpamToChatId)
                return;

            try
            {
                var name = chat.Title ?? string.Join(" ", new[] { chat.FirstName, chat.LastName }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));
                if (string.IsNullOrWhiteSpace(name))
                    name = chat.Username ?? chat.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await chatsRepository.SaveChatAsync(new ReviewChat(chat.Id, name, DateTimeOffset.UtcNow));
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Unable to update chat catalog for chat {chatId}", chat.Id);
            }
        }

        private static Message? ExtractMessageFromUpdate(Update? update)
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
