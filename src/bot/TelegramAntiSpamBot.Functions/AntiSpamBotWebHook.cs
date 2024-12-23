namespace TelegramAntiSpamBot.Functions
{
    public class AntiSpamBotWebHook(ILogger<AntiSpamBotWebHook> logger,
        ISpamDetectionService detectionService,
        AntiSpamBotRepository repository,
        ITelegramBotClient bot,
        TelegramImageUrlResolver imageUrlResolver,
        IOptions<TelegramBotConfiguration> botConfig)
    {
        [Function("AntiSpamBotWebHook")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            try
            {
                var update = JsonSerializer.Deserialize<Update>(req.Body, JsonBotAPI.Options);

                var message = ExtractMessageFromUpdate(update);

                if (message is { From: { } fromUser }
                    && fromUser.Id != 777000 // forwarded messages from main group (TG user)

                    // database chat for spam collection
                    // helpful for controlling of what was deleted
                    && message.Chat.Id != botConfig.Value.ForwardSpamToChatId)
                {
                    var userMessageCount = await repository.GetUserMessageCount(message.Chat.Id, fromUser.Id);
                    var increaseCounterTask = repository.IncreaseMessageCount(message.Chat.Id, fromUser.Id);
                    var imageUrl = await ExtractUrlImageFromMessage(message);


                    // Step 1. Extract message text
                    var messageContent =  message.Text ?? string.Empty;

                    // Step 2. Extract image text
                    if (imageUrl != null)
                    {
                        var imageText = await detectionService.ExtractImageText(imageUrl);
                        messageContent = MergeMessageContent(messageContent, imageText);
                    }

                    // Step 3. Check message content for spam
                    if (messageContent.Length > 30 || imageUrl is not null)
                    {
                        // Step 3.1 Shortcut. if there is hashed spam message in db
                        // Quick access
                        var contentHash = SHA256.HashData(Encoding.Unicode.GetBytes(messageContent));
                        var hex = Convert.ToHexString(contentHash);
                        var shortcutResult = await repository.IsSpam(hex);

                        if (shortcutResult)
                        {
                            await HandleSpamShort(message.Id, message.Chat.Id);
                            logger.LogInformation("Shortcut is used for spam detection");

                            return req.CreateResponse(HttpStatusCode.NoContent);

                        }

                        // Step 3.2. 
                        // Check message for spam with Open AI

                        var spamDetectionResult = await detectionService.IsSpam(messageContent, userMessageCount, botConfig.Value.DebugAiResponse);

                        logger.LogInformation("SPAM DETECTION RESULT: {probability}", spamDetectionResult.Probability);

                        if (spamDetectionResult.Probability >= 90)
                        {
                            await HandleSpam(message.Id, message.Chat.Id, fromUser.Id, messageContent,
                                spamDetectionResult.Probability.Value, hex);
                        }
                    }

                    await increaseCounterTask;
                }
            }
            catch (Exception e)
            {
                logger.LogError("Error: {e}", e);
            }

            return req.CreateResponse(HttpStatusCode.NoContent);
        }

        private async Task HandleSpamShort(int messageId, long chatId)
        {
            if (botConfig.Value.ForwardSpamToChatId != null)
            {
                await bot.ForwardMessage(new ChatId(botConfig.Value.ForwardSpamToChatId.Value), chatId, messageId);
            }

            await bot.DeleteMessage(chatId, messageId);
        }

        private async Task HandleSpam(int messageId, long chatId, long userId, string messageContent, int probability, string hex)
        {
            var saveTask1 = repository.SaveMessageAsync(new SpamHistoryEntry(chatId,
                userId,
                messageContent,
                probability));
            var saveTask2 = repository.SaveMessageHash(hex);

            await HandleSpamShort(messageId, chatId);

            await Task.WhenAll(saveTask1, saveTask2);
        }

        private static string MergeMessageContent(string messageText, string imageText) => $"{messageText}\n\r{imageText}";


        private async Task<string?> ExtractUrlImageFromMessage(Message message)
        {
            var photoSize = message.Photo?.LastOrDefault();
            if (photoSize == null) return null;

            var fileInfo = await bot.GetFile(photoSize.FileId);
            return fileInfo is { FilePath.Length: > 0 }
                ? imageUrlResolver.GetImageUrl(fileInfo.FilePath)
                : null;
        }

        private static Message ExtractMessageFromUpdate(Update? update)
        {
            return update switch
            {
                { Type: UpdateType.Message, Message: { } addedMessage } => addedMessage,
                { Type: UpdateType.EditedMessage, EditedMessage: { } editedMessage } => editedMessage,
                _ => throw new InvalidDataException("Incoming update does not have added or edited message")
            };
        }
    }
}
