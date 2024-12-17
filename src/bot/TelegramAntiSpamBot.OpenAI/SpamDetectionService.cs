namespace TelegramAntiSpamBot.OpenAI
{
    public partial class SpamDetectionService(ChatClient chatClient, ILogger<SpamDetectionService> logger)
    {
        private static readonly string Instructions = "Determine the probability that a given message is spam based on specific criteria related to content and user activity.\r\n\r\nYou will be given a JSON input containing a message not only in English and the user's message count. Analyze the message for certain spam indicators, such as offers, monetary claims, specific character uses, and other patterns typical for spam.\r\n\r\n## Steps\r\n\r\n1. **Input Analysis**: \r\n   - Extract the \"Message\" and \"UserMessageCount\" from the JSON input.\r\n   \r\n2. **Spam Indicators** (1 CRITERIA):\r\n   - Check for the presence of spam-related content, such as:\r\n    -  - Severe \r\n     - Swapped characters\r\n     - Offers to make money or earn extra income.  \r\n     - Offers to buy a training course or illegal items. \r\n     - References to making 100 to 500 etc dollars per day or week. \r\n     - Suggestions to move to private messages.\r\n     -  - High\r\n     - Links to external chats. - Severe\r\n     - Links to known resources like yotube, github - Low \r\n     - Participation offers unrelated to IT. \r\n    - - Moderate \r\n     - Mentions of cryptocurrency.  \r\n     - Use of UTF-16 characters.  \r\n   - Note that if characters are swapped (e.g. Russian 'р' with English 'p'), account for these when detecting indicators.\r\n\r\n3. **User Activity Check** (2 CRITERIA):\r\n   - If at least one spam indicator is found, and \"UserMessageCount\" is less than 50, increase the probability due to this low activity threshold.\r\n\r\n4. **Probability Calculation**:\r\n   - Calculate and output the probability as a percentage based on the presence and severity of spam indicators, and user activity.\r\n\r\n## Output Format\r\n\r\n- Output the result in JSON format as follows: `{\"Probability\": <calculated_value>}`. The value should indicate the percentage of the probability that the message is spam.\r\n\r\n## Examples\r\n\r\nInput: \r\n```json\r\n{\r\n  \"Message\": \"заработайте до 500 долларов в неделю\",\r\n  \"UserMessageCount\": 80\r\n}\r\n```\r\nOutput:\r\n```json\r\n{\r\n  \"Probability\": 75\r\n}\r\n```\r\nDo not use markdown for output results\r\n \r\n## Notes\r\n\r\n- Be precise in identifying character replacements and severity assignments as they are crucial in the calculation.\r\n- Consider both content indicators and user activity to derive the final probability.";

        private static readonly ChatCompletionOptions ChatCompletionOptions = new()
        {
            Temperature = 0.00f,
            TopP = 0.95f
        };

        public async Task<SpamRequestResult> IsSpam(MessageType messageType, string userMessage, int userMessagesCount)
        {
            try
            {
                var request = new SpamDetectionRequest(messageType, userMessage, userMessagesCount);
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
