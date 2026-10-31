using TelegramAntiSpamBot.Persistence.DTO;
using TelegramAntiSpamBot.Persistence.Repositories;

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

                var historyTask = SaveHistoryAsync(message);
                var registerChatTask = RegisterChatAsync(update);
                var persistenceTask = Task.WhenAll(historyTask, registerChatTask);
                HttpResponseData response;
                try
                {
                    response = await ProcessMessageAsync(req, update, message);
                }
                finally
                {
                    await persistenceTask;
                }

                if (!await historyTask)
                    response.StatusCode = HttpStatusCode.InternalServerError;
                return response;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unable to process Telegram update");
                return req.CreateResponse(HttpStatusCode.NoContent);
            }
        }

        private async Task<bool> SaveHistoryAsync(Message message)
        {
            try
            {
                await messageHistoryRepository.SaveAsync(new HistoryMessage(
                    message.Chat.Id, message.MessageId, message.Date.ToUniversalTime().Ticks,
                    new HistoryMessageContent(GetHistoryAuthor(message),
                        message.ReplyToMessage is { } reply
                            ? new HistoryMessageReply(reply.MessageId, GetHistoryAuthor(reply),
                                reply.Text ?? reply.Caption ?? string.Empty)
                            : null,
                        message.Text ?? message.Caption ?? string.Empty)));
                return true;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unable to save message history for chat {chatId}", message.Chat.Id);
                return false;
            }
        }

        private async Task<HttpResponseData> ProcessMessageAsync(HttpRequestData req, Update? update, Message message)
        {
            try
            {
                if (update != null && commandProcessor.IsCommand(update.Message?.Text))
                {
                    await commandProcessor.ProcessCommand(update);
                    return req.CreateResponse(HttpStatusCode.NoContent);
                }

                if (message is { From: { } fromUser }
                    && fromUser.Id != 777000 // forwarded messages from main group (TG user)

                    // database chat for spam collection
                    // helpful for controlling of what was deleted
                    && message.Chat.Id != botConfig.Value.ForwardSpamToChatId)
                {
                    var userMessageCount = await messageCountRepository.GetUserMessageCount(message.Chat.Id, fromUser.Id);
                    var increaseCounterTask = messageCountRepository.IncreaseMessageCount(message.Chat.Id, fromUser.Id);
                    var imageUrl = await ExtractUrlImageFromMessage(message);


                    // Step 1. Extract message text
                    var originalText = message.Text ?? message.Caption ?? string.Empty;
                    var messageContent = originalText;
                    var imageText = string.Empty;

                    // Step 2. Extract image text
                    if (imageUrl != null)
                    {
                        imageText = await detectionService.ExtractImageText(imageUrl);
                        messageContent = MergeMessageContent(messageContent, imageText);
                    }

                    logger.LogInformation("Message content: {messageContent}", messageContent);

                    // Step 3. Check message content for spam
                    if ((messageContent.Length > 20 || imageUrl is not null) && userMessageCount < 100)
                    {
                        // Step 3.1 Shortcut. if there is hashed spam message in db
                        // Quick access
                        var sanitizedContent = MessageContentSanitizer.Sanitize(messageContent);
                        var contentHash = SHA256.HashData(Encoding.Unicode.GetBytes(sanitizedContent));
                        var hex = Convert.ToHexString(contentHash);
                        var shortcutResult = sanitizedContent.Length > 0
                            ? await spamHashRepository.IsSpam(hex)
                            : ShortcutCheckResult.NotFound;

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
                        var spamDetectionResult = await detectionService.IsSpamAsync(messageContent, userMessageCount, botConfig.Value.DebugAiResponse);

                        logger.LogInformation("SPAM DETECTION RESULT: {probability}. EXPLANATION: {explanation}",
                            spamDetectionResult.Probability, spamDetectionResult.Explanation);

                        if (spamDetectionResult.ResultType == ResultType.Error)
                        {
                            logger.LogError("Spam detection service returned error for message: {messageContent}", messageContent);
                            return req.CreateResponse(HttpStatusCode.InternalServerError);
                        }

                        if (spamDetectionResult.Probability >= 95)
                        {
                            await HandleSpam(message, originalText, imageText,
                                spamDetectionResult, sanitizedContent.Length > 0 ? hex : string.Empty);
                        }
                        else if (sanitizedContent.Length > 0)
                        {
                            await spamHashRepository.SaveMessageHash(hex, false);
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
            var name = string.Join(" ", new[] { user.FirstName, user.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

            await probablySpamRepository.SaveReviewMessageAsync(new ReviewMessage(chatId, message.MessageId,
                user.Id, name, originalText, imageText, hex, message.Date.ToUniversalTime().Ticks));

            await HandleSpamShort(message);

            if (botConfig.Value.ForwardSpamToChatId != null && !string.IsNullOrEmpty(spamDetectionResult.Explanation))
            {
                await bot.SendMessage(new ChatId(botConfig.Value.ForwardSpamToChatId.Value), spamDetectionResult.Explanation);
            }
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
