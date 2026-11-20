namespace TelegramAntiSpamBot.Functions
{
    public class SpamMessageProcessor(
        ILogger<SpamMessageProcessor> logger,
        ISpamDetectionService detectionService,
        IMessageHistoryRepository messageHistoryRepository,
        IProbablySpamRepository probablySpamRepository,
        IMessageCountRepository messageCountRepository,
        ISpamHashRepository spamHashRepository,
        ISpamStatsRepository spamStatsRepository,
        ITelegramBotClient bot,
        IOptions<TelegramBotConfiguration> botConfig) : IReportedMessageProcessor
    {
        private const int MaxShortMessageLength = 20;
        private const int TrustedSenderMessageCount = 100;
        private const int SpamProbabilityThreshold = 95;

        public async Task ProcessReportedAsync(Message message)
        {
            if (TelegramMessageSource.IsServiceOrAutomaticForward(message))
                return;

            await ProcessMessageAsync(message, UpdateType.Message, 0, isReported: true);
        }

        public async Task<bool> ProcessMessageAsync(Message message, UpdateType updateType, int updateId, bool isReported = false)
        {
            Task counterWrite = Task.CompletedTask;
            try
            {
                var originalText = GetMessageText(message);
                var image = await AnalyzePhotoAsync(message);
                var content = new ModerationContent(originalText, image);
                var isSpam = false;

                if (message.From is { } sender)
                {
                    var priorMessageCount = await messageCountRepository.GetUserMessageCount(message.Chat.Id, sender.Id);
                    if (!isReported)
                        counterWrite = messageCountRepository.IncreaseMessageCount(message.Chat.Id, sender.Id);

                    if (ShouldCheckForSpam(content, priorMessageCount, isReported))
                        isSpam = await ClassifyAndModerateAsync(message, content, priorMessageCount, isReported);
                }

                if (!isSpam && !isReported)
                    await SaveHistoryAsync(message, content.Payload, updateType, updateId);

                return isSpam;
            }
            catch (Exception e)
            {
                logger.LogError("Error: {e}", e);
                throw;
            }
            finally
            {
                await counterWrite;
            }
        }

        private static bool ShouldCheckForSpam(ModerationContent content, int priorMessageCount, bool isReported)
        {
            if (!content.Payload.HasContent) return false;
            if (isReported) return true;

            return priorMessageCount < TrustedSenderMessageCount
                && (content.Image is not null || content.Payload.ContentLength > MaxShortMessageLength);
        }

        private async Task<bool> ClassifyAndModerateAsync(Message message, ModerationContent content, int priorMessageCount, bool isReported)
        {
            var contentHash = ComputeContentHash(content.Payload);
            var cached = !isReported && contentHash is not null
                ? await spamHashRepository.IsSpam(contentHash)
                : ShortcutCheckResult.NotFound;

            if (cached.IsShortCut)
            {
                var isSpam = cached.IsSpam!.Value;
                logger.LogInformation("Shortcut is used for spam detection. Message is spam {isSpam}", isSpam);
                if (isSpam)
                    await EnforceSpamDecisionAsync(message);
                return isSpam;
            }

            var result = await detectionService.IsSpamAsync(content.Payload, priorMessageCount, isReported);
            logger.LogInformation("Spam detection result: {resultType}, probability: {probability}", result.ResultType, result.Probability);
            if (result.ResultType == ResultType.Error)
                throw new InvalidOperationException("Spam detection service returned an error.");

            if (result.Probability >= SpamProbabilityThreshold)
            {
                await EnforceSpamDecisionAsync(message);
                await SaveReviewAndNotifyArchiveAsync(message, content, contentHash ?? string.Empty, result.Explanation);
                return true;
            }

            if (!isReported && contentHash is not null)
                await spamHashRepository.SaveMessageHash(contentHash, false);
            return false;
        }

        private static string? ComputeContentHash(MessagePayload payload)
        {
            static string? Sanitize(string? value) => NullIfEmpty(MessageContentSanitizer.Sanitize(value ?? string.Empty));

            var sanitized = payload with
            {
                ImageDescription = Sanitize(payload.ImageDescription),
                ImageText = Sanitize(payload.ImageText),
                UserMessage = Sanitize(payload.UserMessage)
            };

            if (!sanitized.HasContent) return null;

            // Stored hashes use the UTF-16LE bytes of the serialized payload.
            var json = JsonSerializer.Serialize(sanitized);
            return Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(json)));
        }

        private async Task EnforceSpamDecisionAsync(Message message)
        {
            var chatId = message.Chat.Id;
            var messageId = message.MessageId;
            var userId = message.From!.Id;

            if (botConfig.Value.ForwardSpamToChatId is { } archiveChatId)
            {
                try
                {
                    await bot.ForwardMessage(new ChatId(archiveChatId), chatId, messageId);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Unable to forward the message to chat id {chatId}", chatId);
                }
            }

            try
            {
                await bot.DeleteMessage(chatId, messageId);
            }
            catch (ApiRequestException exception) when (exception.ErrorCode == 400 &&
                string.Equals(exception.Message, "Bad Request: message to delete not found", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Spam message {messageId} in chat {chatId} is already absent", messageId, chatId);
            }

            var messageAge = DateTime.UtcNow - message.Date;
            var updateStats = spamStatsRepository.UpdateChannelSpamStats(chatId, (int)messageAge.TotalMilliseconds);
            var restrictSender = bot.RestrictChatMember(chatId, userId, new ChatPermissions
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

            await Task.WhenAll(updateStats, restrictSender);
        }

        private async Task SaveReviewAndNotifyArchiveAsync(Message message, ModerationContent content, string contentHash, string? explanation)
        {
            var replyTo = message.ReplyToMessage is { } reply
                ? new ReviewMessageReplyTo(reply.MessageId, GetMessageFrom(reply), GetMessageText(reply))
                : null;

            var review = new ReviewMessage(message.Chat.Id, message.MessageId, message.From!.Id, GetMessageFrom(message)!,
                replyTo, content.OriginalText, content.Image?.ToMessageContent() ?? string.Empty, contentHash,
                message.Date.ToUniversalTime().Ticks);

            await probablySpamRepository.SaveReviewMessageAsync(review);

            if (botConfig.Value.ForwardSpamToChatId is { } archiveChatId && !string.IsNullOrEmpty(explanation))
                await bot.SendMessage(new ChatId(archiveChatId), explanation);
        }

        private async Task SaveHistoryAsync(Message message, MessagePayload payload, UpdateType updateType, int updateId)
        {
            if (!payload.HasContent) return;

            try
            {
                var replyTo = message.ReplyToMessage is { } reply
                    ? new HistoryMessageReply(reply.MessageId, GetMessageFrom(reply), GetMessageText(reply))
                    : null;
                var content = new HistoryMessageContent(GetMessageFrom(message), replyTo, payload);
                var historyUpdateType = updateType switch
                {
                    UpdateType.EditedMessage => HistoryUpdateType.EditedMessage,
                    UpdateType.ChannelPost => HistoryUpdateType.ChannelPost,
                    UpdateType.EditedChannelPost => HistoryUpdateType.EditedChannelPost,
                    _ => HistoryUpdateType.Message
                };
                await messageHistoryRepository.SaveAsync(new HistoryMessage(message.Chat.Id, message.MessageId,
                    message.Date.ToUniversalTime().Ticks, content, historyUpdateType, updateId));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unable to save message history for chat {chatId}", message.Chat.Id);
                throw;
            }
        }

        private static string GetMessageText(Message message)
        {
            var text = message.Text ?? message.Caption ?? string.Empty;
            var entities = message.Text is not null ? message.Entities : message.CaptionEntities;
            if (entities is null)
                return text;

            // Telegram entity offsets and .NET string indices both use UTF-16 code units.
            foreach (var entity in entities
                .Where(entity => entity.Type == MessageEntityType.TextLink && !string.IsNullOrWhiteSpace(entity.Url)
                    && entity.Offset >= 0 && entity.Length > 0 && entity.Offset <= text.Length
                    && entity.Length <= text.Length - entity.Offset)
                .DistinctBy(entity => (entity.Offset, entity.Length, entity.Url))
                .OrderByDescending(entity => entity.Offset + entity.Length))
            {
                text = text.Insert(entity.Offset + entity.Length, $" ({entity.Url})");
            }

            return text;
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private async Task<ImageAnalysisResult?> AnalyzePhotoAsync(Message message)
        {
            if (message.Photo?.LastOrDefault() is not { } photo) return null;

            var file = await bot.GetFile(photo.FileId);
            if (string.IsNullOrEmpty(file.FilePath)) return null;

            using var stream = new MemoryStream();
            try
            {
                await bot.DownloadFile(file.FilePath, stream);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Telegram photo download failed ({ErrorType}); continuing without image content", ex.GetType().Name);
                return new ImageAnalysisResult();
            }

            return await detectionService.ExtractImageText(new BinaryData(stream.GetBuffer().AsMemory(0, (int)stream.Length)));
        }

        private static MessageFrom? GetMessageFrom(Message message)
        {
            if (message.From is { } user)
                return new MessageFrom(user.Id,
                    string.Join(" ", new[] { user.FirstName, user.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))),
                    user.Username);
            if (message.SenderChat is { } chat)
                return new MessageFrom(chat.Id, chat.Title ?? string.Empty, chat.Username);
            return null;
        }

        private sealed record ModerationContent(string OriginalText, ImageAnalysisResult? Image)
        {
            public MessagePayload Payload { get; } = new(
                ImageDescription: NullIfEmpty(Image?.ImageDescription),
                ImageText: NullIfEmpty(Image?.TextOnImage),
                UserMessage: NullIfEmpty(OriginalText));
        }
    }
}
