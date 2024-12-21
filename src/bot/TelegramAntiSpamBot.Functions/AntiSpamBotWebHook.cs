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
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
        {
            try
            {
                var update = JsonSerializer.Deserialize<Update>(req.BodyReader.AsStream(), JsonBotAPI.Options);

                Message? message = ExtractMessageFromUpdate(update);
                 
                if (message is { From: { } fromUser }
                    && fromUser.Id != 777000 // forwarded messages from main group (TG user)
                    && message.Chat.Id != -1002395980780) // my database channel for spam collection
                {
                    var userMessageCount = await repository.GetUserMessageCount(message.Chat.Id, fromUser.Id);
                    var increaseCounterTask = repository.IncreaseMessageCount(message.Chat.Id, fromUser.Id);
                    var imageUrl = await ExtractUrlImageFromMessage(message);

                    if (message.Text?.Length > 30 || imageUrl is not null)
                    {
                        var messageText = message.Text ?? string.Empty;
                        var spamDetectionResult = await detectionService.IsSpam(messageText, userMessageCount, imageUrl, botConfig.Value.DebugAiResponse);

                        logger.LogInformation("SPAM DETECTION RESULT: {probability}", spamDetectionResult.Probability);

                        if (spamDetectionResult.Probability >= 90)
                        {
                            var saveTask = repository.SaveMessageAsync(new SpamHistoryEntry(message.Chat.Id,
                                                                message.From.Id,
                                                                messageText,
                                                                spamDetectionResult.Probability.Value));

                            await bot.ForwardMessage(new ChatId(-1002395980780), message.Chat.Id, message.Id);
                            await bot.DeleteMessage(message.Chat.Id, message.Id);
                            
                            await saveTask;
                        }
                    }

                    await increaseCounterTask;
                }
            }
            catch (Exception e)
            {
                logger.LogError("Error: {e}", e);
            }

            return new OkObjectResult(null);
        }

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
