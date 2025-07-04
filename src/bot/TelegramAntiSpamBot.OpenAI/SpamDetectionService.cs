﻿namespace TelegramAntiSpamBot.OpenAI
{
    internal sealed partial class SpamDetectionService(
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
            MaxOutputTokenCount = 300
        };

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
                LogDebugSpamDetectionAiResponse(logger, responseText);

                var result = JsonSerializer.Deserialize<SpamDetectionResult>(responseText);

                if (result?.Probability is { } number)
                {
                    if (!explainDecision) return new SpamRequestResult(ResultType.Evaluated, number);

                    var explanationResult = JsonSerializer.Deserialize<SpamExplanationResult>(responseText);

                    return new SpamRequestResult(ResultType.Evaluated, number, explanationResult?.Explanation);
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

            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uriResult)
                || uriResult.Scheme != Uri.UriSchemeHttps) return string.Empty;

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

            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        [LoggerMessage(LogLevel.Debug, "Spam Detection AI Service Request: {request}")]
        private static partial void LogDebugSpamDetectionAiRequest(ILogger<SpamDetectionService> logger, string request);

        [LoggerMessage(LogLevel.Debug, "Spam Detection AI Service Response: {response}")]
        private static partial void LogDebugSpamDetectionAiResponse(ILogger<SpamDetectionService> logger, string response);

        [LoggerMessage(LogLevel.Debug, "Image Recognition AI Service Response: {response}")]
        private static partial void LogDebugImageRecognitionAiResponse(ILogger<SpamDetectionService> logger, string response);

        [LoggerMessage(LogLevel.Debug, "Image Recognition AI Service Request: ImageUrl: {imageUrl}.")]
        private static partial void LogDebugImageRecognitionAiRequest(ILogger<SpamDetectionService> logger, string imageUrl);
    }
}
