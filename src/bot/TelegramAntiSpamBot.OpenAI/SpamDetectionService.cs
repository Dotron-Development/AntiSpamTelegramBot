namespace TelegramAntiSpamBot.OpenAI
{
    internal partial class SpamDetectionService(
        ChatClient chatClient,
        SpamDetectionInstructions instructions,
        ILogger<SpamDetectionService> logger) : ISpamDetectionService
    {
        private static readonly ChatCompletionOptions ChatCompletionOptions = new()
        {
            Temperature = 0.00f,
            TopP = 0.95f,
            PresencePenalty = 0,
            FrequencyPenalty = 0,
            MaxOutputTokenCount = 100
        };

        [GeneratedRegex(@"\{\s*""Probability""\s*:\s*(100|[1-9]?[0-9])\s*\}")]
        private static partial Regex ResultRegex();

        public async Task<SpamRequestResult> IsSpam(
            string userMessage, 
            int userMessagesCount, 
            string? imageUrl = null, 
            bool explainDecision = false)
        {
            try
            {
                var instr = await instructions.GetSpamDetectionInstructions(explainDecision);               
                string? imageDescription = null;

                // Step 1 - describe image if attached
                if (imageUrl != null && Uri.TryCreate(imageUrl, UriKind.Absolute, out var uriResult) 
                                     && uriResult.Scheme == Uri.UriSchemeHttps)
                {
                    var imageRecognitionInstructions = await instructions.GetImageAnalyzerInstructions();
                    var imageRecognitionCompletion = await chatClient.CompleteChatAsync(
                    [
                        new SystemChatMessage(imageRecognitionInstructions),
                        new UserChatMessage(
                            ChatMessageContentPart.CreateImagePart(uriResult))

                    ], ChatCompletionOptions);

                    imageDescription = imageRecognitionCompletion.Value.Content[0].Text;
                }

                // Step 2 - Spam analysis
                var requestJson = JsonSerializer.Serialize(new SpamDetectionRequest(userMessage, userMessagesCount, imageDescription));
                ChatCompletion completion = await chatClient.CompleteChatAsync(
                [
                    new SystemChatMessage(instr),
                    new UserChatMessage(requestJson)

                ], ChatCompletionOptions);

                var responseText = completion.Content[0].Text;
                
                LogDebugAiResponse(logger, requestJson, imageUrl, completion.Content[0].Text);

                if (ResultRegex().IsMatch(responseText))
                {
                    var json = ResultRegex().Match(responseText).Groups[0].Value;
                    var result = JsonSerializer.Deserialize<SpamDetectionResult>(json);
                    if (result?.Probability is { } number)
                    {
                        if (!explainDecision) return new SpamRequestResult(ResultType.Evaluated, number);

                        var explanation = ResultRegex().Replace(responseText, string.Empty);
                        return new SpamRequestResult(ResultType.Evaluated, number, explanation);

                    }
                }

                logger.LogWarning("Unexpected answer from AI: {content}", completion.Content[0].Text);
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

        [LoggerMessage(LogLevel.Debug, "AI Service Request: {request}. ImageUrl: {imageUrl}. Response: {response}")]
        private static partial void LogDebugAiResponse(ILogger logger, string request, string? imageUrl, string response);
    }
}
