namespace TelegramAntiSpamBot.Functions
{
    public class AntiSpamBotWebHook(ILogger<AntiSpamBotWebHook> logger,
        ISpamDetectionService detectionService,
        IAntiSpamBotRepository repository,
        ITelegramBotClient bot,
        TelegramImageUrlResolver imageUrlResolver,
        ICommandProcessor commandProcessor,
        IOptions<TelegramBotConfiguration> botConfig)
    {
        [Function("AntiSpamBotWebHook")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            try
            {
                var update = JsonSerializer.Deserialize<Update>(req.Body, JsonBotAPI.Options);

                if (update != null && commandProcessor.IsCommand(update.Message?.Text))
                {
                    await commandProcessor.ProcessCommand(update);
                    return req.CreateResponse(HttpStatusCode.NoContent);
                }

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
                    var messageContent = message.Text ?? string.Empty;

                    // Step 2. Extract image text
                    if (imageUrl != null)
                    {
                        var imageText = await detectionService.ExtractImageText(imageUrl);
                        messageContent = MergeMessageContent(messageContent, imageText);
                    }

                    logger.LogInformation("Message content: {messageContent}", messageContent);

                    // Step 3. Check message content for spam
                    if (messageContent.Length > 30 || imageUrl is not null)
                    {
                        // Step 3.1 Shortcut. if there is hashed spam message in db
                        // Quick access
                        var contentHash = SHA256.HashData(Encoding.Unicode.GetBytes(messageContent));
                        var hex = Convert.ToHexString(contentHash);
                        var shortcutResult = await repository.IsSpam(hex);

                        if (shortcutResult.IsShortCut)
                        {
                            logger.LogInformation("Shortcut is used for spam detection. Message is spam {isSpam}", shortcutResult.IsSpam);

                            if (shortcutResult.IsSpam!.Value)
                            {
                                await HandleSpamShort(message);
                            }

                            return req.CreateResponse(HttpStatusCode.NoContent);
                        }

                        // Step 3.2. 
                        // Check message for spam with Open AI
                        var spamDetectionResult = await detectionService.IsSpam(messageContent, userMessageCount, botConfig.Value.DebugAiResponse);

                        logger.LogInformation("SPAM DETECTION RESULT: {probability}. EXPLANATION: {explanation}",
                            spamDetectionResult.Probability, spamDetectionResult.Explanation);

                        if (spamDetectionResult.ResultType == ResultType.Error)
                        {
                            logger.LogError("Spam detection service returned error for message: {messageContent}", messageContent);
                            return req.CreateResponse(HttpStatusCode.InternalServerError);
                        }

                        if (spamDetectionResult.Probability >= 90)
                        {
                            await HandleSpam(message, messageContent,
                                spamDetectionResult.Probability.Value, hex);
                        }
                        else
                        {
                            await repository.SaveMessageHash(hex, false);
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

        private async Task HandleSpamShort(Message message)
        {
            var chatId = message.Chat.Id;
            var messageId = message.MessageId;
            var userId = message.From!.Id;

            if (botConfig.Value.ForwardSpamToChatId != null)
            {
                try
                {
                    await bot.ForwardMessage(new ChatId(botConfig.Value.ForwardSpamToChatId.Value), chatId, messageId);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Unable to forward the message to chat id {chatId}", chatId);
                }
            }

            await bot.DeleteMessage(chatId, messageId);

            var diff = DateTime.UtcNow - message.Date;
            var t1 = repository.UpdateChannelSpamStats(chatId, (int)diff.TotalMilliseconds);
            var t2 = bot.RestrictChatMember(chatId, userId, new ChatPermissions()
            {
                CanSendPhotos = false,
                CanSendMessages = false,
                CanSendOtherMessages = false,
                CanSendAudios = false,
                CanSendVideoNotes = false,
                CanSendDocuments = false,
                CanSendVideos = false,
                CanSendPolls = false,
                CanSendVoiceNotes = false,
                CanAddWebPagePreviews = false,
                CanChangeInfo = false,
                CanInviteUsers = false,
                CanManageTopics = false,
                CanPinMessages = false
            }, false, DateTime.UtcNow.AddHours(3));

            await Task.WhenAll(t1, t2);
        }

        private async Task HandleSpam(Message message, string messageContent, int probability, string hex)
        {
            var chatId = message.Chat.Id;
            var userId = message.From!.Id;

            var saveTask1 = repository.SaveMessageAsync(chatId,
                userId,
                messageContent,
                probability);

            var saveTask2 = repository.SaveMessageHash(hex, true);

            var t3 = HandleSpamShort(message);

            await Task.WhenAll(saveTask1, saveTask2, t3);
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
