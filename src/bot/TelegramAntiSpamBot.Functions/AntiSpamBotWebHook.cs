using System.Net;
using Microsoft.Azure.Functions.Worker.Http;

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

                            if (botConfig.Value.ForwardSpamToChatId != null)
                            {
                                await bot.ForwardMessage(new ChatId(botConfig.Value.ForwardSpamToChatId.Value), message.Chat.Id, message.Id);
                            }
                            
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

            return req.CreateResponse(HttpStatusCode.NoContent);
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
