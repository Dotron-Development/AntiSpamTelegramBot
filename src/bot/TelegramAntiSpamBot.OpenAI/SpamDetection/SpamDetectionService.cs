
namespace TelegramAntiSpamBot.OpenAI
{
    internal sealed partial class SpamDetectionService(
        [FromKeyedServices("SpamDetector")] ChatClient spamDetector,
        [FromKeyedServices("ImageAnalyzer")] ChatClient imageAnalyzer,
        SpamDetectionInstructions instructions,
        ILogger<SpamDetectionService> logger) : ISpamDetectionService
    {
        // The SDK mutates options during request preparation, so each call needs its own instance.
        private static ChatCompletionOptions CreateSpamDetectionOptions() => new()
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "spam_detection",
                jsonSchema: BinaryData.FromString("""
                    {
                      "type": "object",
                      "properties": {
                        "Probability": { "type": ["integer", "null"], "minimum": 0, "maximum": 100 },
                        "Explanation": { "type": "string" }
                      },
                      "required": ["Probability", "Explanation"],
                      "additionalProperties": false
                    }
                    """),
                jsonSchemaIsStrict: true)
        };

        private static ChatCompletionOptions CreateImageAnalysisOptions() => new()
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "image_analysis",
                jsonSchema: BinaryData.FromString("""
                    {
                      "type": "object",
                      "properties": {
                        "TextOnImage": { "type": "string" },
                        "ImageDescription": { "type": "string" }
                      },
                      "required": ["TextOnImage", "ImageDescription"],
                      "additionalProperties": false
                    }
                    """),
                jsonSchemaIsStrict: true)
        };

        public async Task<SpamRequestResult> IsSpamAsync(
            MessagePayload userMessage,
            int userMessagesCount,
            bool isReported = false)
        {
            try
            {
                var requestJson = JsonSerializer.Serialize(new SpamDetectionRequest(userMessage, userMessagesCount, isReported));

                LogDebugSpamDetectionAiRequest(logger, requestJson);

                var instr = await instructions.GetSpamDetectionInstructions();
                ChatCompletion completion = await spamDetector.CompleteChatAsync(
                [
                    new SystemChatMessage(instr),
                    new UserChatMessage(requestJson)

                ], CreateSpamDetectionOptions());

                if (completion.FinishReason != ChatFinishReason.Stop || completion.Content.Count == 0)
                {
                    logger.LogWarning("Spam detection returned no usable completion. Finish reason: {finishReason}", completion.FinishReason);
                    return new SpamRequestResult(ResultType.Error);
                }

                var responseText = string.Concat(completion.Content.Select(part => part.Text));
                LogDebugSpamDetectionAiResponse(logger, responseText);

                var result = JsonSerializer.Deserialize<SpamDetectionResult>(responseText);

                if (result?.Probability is >= 0 and <= 100 && !string.IsNullOrWhiteSpace(result.Explanation))
                {
                    return new SpamRequestResult(ResultType.Evaluated, result.Probability, result.Explanation);
                }

                logger.LogWarning("Unexpected answer from AI: {content}", responseText);
                return new SpamRequestResult(ResultType.Error);
            }
            catch (ClientResultException e) when (e.Status == 400)
            {
                logger.LogError(e, "Open AI filter blocked the message: {userMessage}", userMessage);
                logger.LogError(e.ToString());
                return new SpamRequestResult(ResultType.Error);
            }
            catch (Exception e)
            {
                logger.LogError("Error occurred during spam detection process: {e}", e);
                return new SpamRequestResult(ResultType.Error);
            }
        }

        public async Task<ImageAnalysisResult> ExtractImageText(string imageUrl)
        {
            LogDebugImageRecognitionAiRequest(logger, imageUrl);

            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uriResult)
                || uriResult.Scheme != Uri.UriSchemeHttps) return new ImageAnalysisResult();

            try
            {
                var imageRecognitionInstructions = await instructions.GetImageAnalyzerInstructions();
                ChatCompletion completion = await imageAnalyzer.CompleteChatAsync(
                [
                    new SystemChatMessage(imageRecognitionInstructions),
                    new UserChatMessage(
                        ChatMessageContentPart.CreateImagePart(uriResult))

                ], CreateImageAnalysisOptions());

                if (completion.FinishReason != ChatFinishReason.Stop || completion.Content.Count == 0)
                {
                    logger.LogWarning("Image analysis returned no usable completion. Finish reason: {finishReason}", completion.FinishReason);
                    return new ImageAnalysisResult();
                }

                var imageResponse = string.Concat(completion.Content.Select(part => part.Text));
                if (string.IsNullOrWhiteSpace(imageResponse))
                {
                    logger.LogWarning("Image analysis returned empty text");
                    return new ImageAnalysisResult();
                }

                LogDebugImageRecognitionAiResponse(logger, imageResponse);

                return JsonSerializer.Deserialize<ImageAnalysisResult>(imageResponse) ?? new ImageAnalysisResult();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Image analysis failed; continuing without image content");
                return new ImageAnalysisResult();
            }
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
