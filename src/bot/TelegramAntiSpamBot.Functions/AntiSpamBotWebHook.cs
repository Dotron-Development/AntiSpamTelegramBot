
namespace TelegramAntiSpamBot.Functions
{
    public class AntiSpamBotWebHook(ILogger<AntiSpamBotWebHook> logger,
        ISpamDetectionService detectionService,
        IChatsRepository chatsRepository,
        IMessageHistoryRepository messageHistoryRepository,
        IProbablySpamRepository probablySpamRepository,
        IMessageCountRepository messageCountRepository,
        ISpamHashRepository spamHashRepository,
        ISpamStatsRepository spamStatsRepository,
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

                var message = ExtractMessageFromUpdate(update);
                if (message is null)
                    return req.CreateResponse(HttpStatusCode.NoContent);

                var registerChatTask = RegisterChatAsync(update);

                try
                {
                    if (update is not null && commandProcessor.IsCommand(update.Message?.Text))
                        await commandProcessor.ProcessCommand(update);
                    else
                        await ProcessMessageAsync(message, update!.Type, update.Id);
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

        private async Task SaveHistoryAsync(Message message, MessagePayload payload, UpdateType updateType, int updateId)
        {
            if (!payload.HasContent)
                return;

            try
            {
                await messageHistoryRepository.SaveAsync(new HistoryMessage(
                    message.Chat.Id, message.MessageId,
                    message.Date.ToUniversalTime().Ticks,
                    new HistoryMessageContent(GetHistoryAuthor(message),
                        message.ReplyToMessage is { } reply
                            ? new HistoryMessageReply(reply.MessageId, GetHistoryAuthor(reply),
                                reply.Text ?? reply.Caption ?? string.Empty)
                            : null,
                        payload), updateType switch
                        {
                            UpdateType.EditedMessage => HistoryUpdateType.EditedMessage,
                            UpdateType.ChannelPost => HistoryUpdateType.ChannelPost,
                            UpdateType.EditedChannelPost => HistoryUpdateType.EditedChannelPost,
                            _ => HistoryUpdateType.Message
                        }, updateId));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unable to save message history for chat {chatId}", message.Chat.Id);
                throw;
            }
        }

        private async Task<bool> ProcessMessageAsync(Message message, UpdateType updateType, int updateId)
        {
            try
            {
                var isSpam = false;
                var originalText = message.Text ?? message.Caption ?? string.Empty;

                var imageUrl = message.Chat.Id != botConfig.Value.ForwardSpamToChatId
                    ? await ExtractUrlImageFromMessage(message)
                    : null;
                var imageAnalysis = imageUrl is not null
                    ? await detectionService.ExtractImageText(imageUrl)
                    : null;

                var imageText = imageAnalysis?.ToMessageContent() ?? string.Empty;
                var messageContent = new MessagePayload(
                    ImageDescription: NullIfEmpty(imageAnalysis?.ImageDescription),
                    ImageText: NullIfEmpty(imageAnalysis?.TextOnImage),
                    UserMessage: NullIfEmpty(originalText));

                if (message is { From: { } fromUser }
                    && fromUser.Id != 777000 // forwarded messages from main group (TG user)

                    // database chat for spam collection
                    // helpful for controlling of what was deleted
                    && message.Chat.Id != botConfig.Value.ForwardSpamToChatId)
                {
                    var userMessageCount = await messageCountRepository.GetUserMessageCount(message.Chat.Id, fromUser.Id);
                    var increaseCounterTask = messageCountRepository.IncreaseMessageCount(message.Chat.Id, fromUser.Id);
                    logger.LogInformation("Message content: {messageContent}", messageContent);

                    // Step 3. Check message content for spam
                    if (messageContent.HasContent
                        && (messageContent.ContentLength > 20 || imageUrl is not null) && userMessageCount < 100)
                    {
                        // Step 3.1 Shortcut. if there is hashed spam message in db
                        // Quick access
                        var sanitizedPayload = messageContent with
                        {
                            ImageDescription = NullIfEmpty(MessageContentSanitizer.Sanitize(messageContent.ImageDescription ?? string.Empty)),
                            ImageText = NullIfEmpty(MessageContentSanitizer.Sanitize(messageContent.ImageText ?? string.Empty)),
                            UserMessage = NullIfEmpty(MessageContentSanitizer.Sanitize(messageContent.UserMessage ?? string.Empty))
                        };
                        var sanitizedContent = JsonSerializer.Serialize(sanitizedPayload);
                        var contentHash = SHA256.HashData(Encoding.Unicode.GetBytes(sanitizedContent));
                        var hex = Convert.ToHexString(contentHash);
                        var shortcutResult = sanitizedPayload.HasContent
                            ? await spamHashRepository.IsSpam(hex)
                            : ShortcutCheckResult.NotFound;

                        if (shortcutResult.IsShortCut)
                        {
                            logger.LogInformation("Shortcut is used for spam detection. Message is spam {isSpam}", shortcutResult.IsSpam);

                            if (shortcutResult.IsSpam!.Value)
                            {
                                await HandleSpamShort(message);
                                return true;
                            }

                            await SaveHistoryAsync(message, messageContent, updateType, updateId);
                            return false;
                        }

                        // Step 3.2. 
                        // Check message for spam with Open AI
                        var spamDetectionResult = await detectionService.IsSpamAsync(messageContent, userMessageCount, botConfig.Value.DebugAiResponse);

                        logger.LogInformation("SPAM DETECTION RESULT: {probability}. EXPLANATION: {explanation}",
                            spamDetectionResult.Probability, spamDetectionResult.Explanation);

                        if (spamDetectionResult.ResultType == ResultType.Error)
                        {
                            logger.LogError("Spam detection service returned error for message: {messageContent}", messageContent);
                            throw new InvalidOperationException("Spam detection service returned an error.");
                        }

                        if (spamDetectionResult.Probability >= 95)
                        {
                            isSpam = true;
                            await HandleSpam(message, originalText, imageText,
                                spamDetectionResult, sanitizedPayload.HasContent ? hex : string.Empty);
                        }
                        else if (sanitizedPayload.HasContent)
                        {
                            await spamHashRepository.SaveMessageHash(hex, false);
                        }
                    }

                    await increaseCounterTask;
                }

                if (!isSpam)
                    await SaveHistoryAsync(message, messageContent, updateType, updateId);

                return isSpam;
            }
            catch (Exception e)
            {
                logger.LogError("Error: {e}", e);
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
            var t1 = spamStatsRepository.UpdateChannelSpamStats(chatId, (int)diff.TotalMilliseconds);
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
            }, false, DateTime.UtcNow.Add(botConfig.Value.SpamRestrictionTimeout));

            await Task.WhenAll(t1, t2);
        }

        private async Task HandleSpam(Message message, string originalText, string imageText, SpamRequestResult spamDetectionResult, string hex)
        {
            var chatId = message.Chat.Id;
            var user = message.From!;
            var from = new ReviewMessageFrom(string.Join(" ", new[] { user.FirstName, user.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part))), user.Username);
            var replyTo = message.ReplyToMessage is { } reply
                ? new ReviewMessageReplyTo(reply.MessageId, GetReviewFrom(reply), reply.Text ?? reply.Caption ?? string.Empty)
                : null;

            await probablySpamRepository.SaveReviewMessageAsync(new ReviewMessage(chatId, message.MessageId,
                user.Id, from, replyTo, originalText, imageText, hex, message.Date.ToUniversalTime().Ticks));

            await HandleSpamShort(message);

            if (botConfig.Value.ForwardSpamToChatId != null && !string.IsNullOrEmpty(spamDetectionResult.Explanation))
            {
                await bot.SendMessage(new ChatId(botConfig.Value.ForwardSpamToChatId.Value), spamDetectionResult.Explanation);
            }
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;


        private async Task<string?> ExtractUrlImageFromMessage(Message message)
        {
            var photoSize = message.Photo?.LastOrDefault();
            if (photoSize == null) return null;

            var fileInfo = await bot.GetFile(photoSize.FileId);
            return fileInfo is { FilePath.Length: > 0 }
                ? imageUrlResolver.GetImageUrl(fileInfo.FilePath)
                : null;
        }

        private static ReviewMessageFrom? GetReviewFrom(Message message)
        {
            if (message.From is { } user)
                return new ReviewMessageFrom(string.Join(" ", new[] { user.FirstName, user.LastName }
                    .Where(part => !string.IsNullOrWhiteSpace(part))), user.Username);
            if (message.SenderChat is { } chat)
                return new ReviewMessageFrom(chat.Title ?? string.Empty, chat.Username);
            return null;
        }

        private static HistoryMessageAuthor? GetHistoryAuthor(Message message)
        {
            if (message.From is { } user)
                return new HistoryMessageAuthor(user.Id,
                    string.Join(" ", new[] { user.FirstName, user.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))),
                    user.Username);
            if (message.SenderChat is { } chat)
                return new HistoryMessageAuthor(chat.Id, chat.Title ?? string.Empty, chat.Username);
            return null;
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
