using MessageType = TelegramAntiSpamBot.OpenAI.MessageType;

namespace TelegramAntiSpamBot.Functions
{
    public class AntiSpamBotWebHook(ILogger<AntiSpamBotWebHook> logger,
        SpamDetectionService detectionService,
        AntiSpamBotRepository repository,
        ITelegramBotClient bot)
    {
        [Function("AntiSpamBotWebHook")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
        {
            try
            {
                var update = JsonSerializer.Deserialize<Update>(req.BodyReader.AsStream(), JsonBotAPI.Options);

                if (update?.Type is UpdateType.Message or UpdateType.EditedMessage
                    && update.Message is { From: { } fromUser }  message 
                    && fromUser.Id != 777000 // forwarded messages from main group (TG user)
                    && message.Chat.Id != -1002395980780) // my database channel for spam collection
                {
                    var userMessageCount = await repository.GetUserMessageCount(message.Chat.Id, fromUser.Id);
                    var increaseCounterTask = repository.IncreaseMessageCount(message.Chat.Id, fromUser.Id);

                    if (message.Text?.Length > 30)
                    {
                        var spamDetectionResult = await detectionService.IsSpam(MessageType.Message, message.Text, userMessageCount);
                        logger.LogInformation("SPAM DETECTION RESULT: {probability}", spamDetectionResult.Probability);

                        if (spamDetectionResult.Probability >= 90)
                        {
                            var saveTask = repository.SaveMessageAsync(new SpamHistoryEntry(message.Chat.Id,
                                                                message.From.Id,
                                                                message.Text,
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
    }
}
