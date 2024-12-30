namespace TelegramAntiSpamBot.OpenAI
{
    internal partial class SpamDetectionService(
        [FromKeyedServices("SpamDetector")] ChatClient spamDetector,
        [FromKeyedServices("ImageAnalyzer")] ChatClient imageAnalyzer,
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
        private static partial Regex SpamResultRegex();

        [GeneratedRegex(@"""TextOnImage""\s*:\s*""(.*?)""")]
        private static partial Regex ImageResultRegex();

        public async Task<SpamRequestResult> IsSpam(
            string userMessage, 
            int userMessagesCount, 
            bool explainDecision = false)
        {
            try
            {
                var requestJson = JsonSerializer.Serialize(new SpamDetectionRequest(userMessage, userMessagesCount));

                LogDebugSpamDetectionAiRequest(logger, requestJson);

                var instr = await instructions.GetSpamDetectionInstructions(explainDecision);
                ChatCompletion completion = await spamDetector.CompleteChatAsync(
                [
                    new SystemChatMessage(instr),
                    new UserChatMessage(requestJson)

                ], ChatCompletionOptions);

                var responseText = completion.Content[0].Text;

                var spamRespMatch = SpamResultRegex().Match(responseText);
                if (spamRespMatch.Success)
                {
                    LogDebugSpamDetectionAiResponse(logger, responseText);

                    var json = spamRespMatch.Groups[0].Value;
                    var result = JsonSerializer.Deserialize<SpamDetectionResult>(json);

                    if (result?.Probability is { } number)
                    {
                        if (!explainDecision) return new SpamRequestResult(ResultType.Evaluated, number);

                        var explanation = SpamResultRegex().Replace(responseText, string.Empty);

                        LogDebugSpamDetectionAiExplanation(logger, explanation);

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

        public async Task<string> ExtractImageText(string imageUrl)
        {
            LogDebugImageRecognitionAiRequest(logger, imageUrl);

            if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uriResult)
                                 && uriResult.Scheme == Uri.UriSchemeHttps)
            {
                var imageRecognitionInstructions = await instructions.GetImageAnalyzerInstructions();
                var imageRecognitionCompletion = await imageAnalyzer.CompleteChatAsync(
                [
                    new SystemChatMessage(imageRecognitionInstructions),
                    new UserChatMessage(
                        ChatMessageContentPart.CreateImagePart(uriResult))

                ], ChatCompletionOptions);

                var imageResponse = imageRecognitionCompletion.Value.Content[0].Text;

                LogDebugImageRecognitionAiResponse(logger, imageResponse);

                var match = ImageResultRegex().Match(imageResponse);

                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return string.Empty;
        }

        [LoggerMessage(LogLevel.Debug, "Spam Detection AI Service Request: {request}")]
        private static partial void LogDebugSpamDetectionAiRequest(ILogger<SpamDetectionService> logger, string request);

        [LoggerMessage(LogLevel.Debug, "Spam Detection AI Service Response: {response}")]
        private static partial void LogDebugSpamDetectionAiResponse(ILogger<SpamDetectionService> logger, string response);

        [LoggerMessage(LogLevel.Debug, "Spam Detection AI Service Response Explanation: {explanation}")]
        private static partial void LogDebugSpamDetectionAiExplanation(ILogger<SpamDetectionService> logger, string explanation);

        [LoggerMessage(LogLevel.Debug, "Image Recognition AI Service Response: {response}")]
        private static partial void LogDebugImageRecognitionAiResponse(ILogger<SpamDetectionService> logger, string response);

        [LoggerMessage(LogLevel.Debug, "Image Recognition AI Service Request: ImageUrl: {imageUrl}.")]
        private static partial void LogDebugImageRecognitionAiRequest(ILogger<SpamDetectionService> logger, string imageUrl);
    }
}
