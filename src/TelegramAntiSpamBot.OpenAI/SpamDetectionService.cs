namespace TelegramAntiSpamBot.OpenAI
{
    public partial class SpamDetectionService(ChatClient chatClient, ILogger<SpamDetectionService> logger)
    {
        private static readonly string Instructions = "You are an anti-spam filter. You need to determine the probability that a message is spam. \r\n\r\nYour input is message and count of user's messages in json format\r\n\r\n{\r\n\"Message\": \"Here you find a message\",\r\n\"UserMessageCount\": 124\r\n}\r\n\r\nThe messages are written in Russian. Spammers often try to deceive you by inserting English letters instead of some Russian ones. Often they replace some cyrilic characters with the latin:\r\nthe Russian р, с, е with the English p,c,e and so on. \r\n\r\nA typical spam message includes (one of) \r\nan offer to make money - high\r\nan offer for additional earnings - high\r\nan offer to buy a training course - high\r\nan offer to buy something illegal - severe\r\nmake 100-500 dollars per day or week - severe\r\ncryptocurrency - high\r\nUTF-16 characters - moderate\r\nmention of going to private messages, pm or in Russian лс, в личку - moderate\r\nlink to another chat in format t.me/channel  or @channel - severe\r\noffer to participate in some project not related to IT - severe\r\n\r\nYou should not consider single item as a complete marker of spam but if you noticed 2 or more it's should be considered as a high or severe probability spam.  \r\nConsider number of messages as the 2nd criteria. If the number of messages <100 then user is a spammer with extrimely high probability. If user has more than 100 then you should consider message as a spam only if it has 3 or more indicators.\r\n\r\nReturn result in percent of probability that message is spam in the following JSON format { \"Probability\": 56 } DO NOT USE ANY markdown";

        private static readonly ChatCompletionOptions ChatCompletionOptions = new()
        {
            Temperature = 0.00f,
            TopP = 0.95f
        };

        public async Task<SpamRequestResult> IsSpam(string userMessage, int userMessagesCount)
        {
            try
            {
                var request = new SpamDetectionRequest(userMessage, userMessagesCount);
                ChatCompletion completion = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage(Instructions),
                    new UserChatMessage(JsonSerializer.Serialize(request)),
                ], ChatCompletionOptions);

                var result = JsonSerializer.Deserialize<SpamDetectionResult>(completion.Content[0].Text);
                if (result?.Probability is { } number)
                {
                    return new SpamRequestResult(ResultType.Evaluated, number);
                }

                logger.LogWarning("Unexpected answer from AI: {content}", completion.Content[1].Text);
                return new SpamRequestResult(ResultType.Error);
            }
            catch (ClientResultException e) when (e.Status == 400)
            {
                logger.LogError(e, "Open AI filter blocked the message: {userMessage}", userMessage);
                return new SpamRequestResult(ResultType.Error);
            }
            catch (Exception e)
            {
                logger.LogError("Error occurred during spam detection process: {e}", e);
                return new SpamRequestResult(ResultType.Error);
            }
        }
    }
}
