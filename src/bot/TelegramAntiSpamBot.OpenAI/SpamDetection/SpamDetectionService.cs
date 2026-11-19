
namespace TelegramAntiSpamBot.OpenAI
{
    internal sealed class SpamDetectionService(
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

                var result = JsonSerializer.Deserialize<SpamDetectionResult>(responseText);

                if (result?.Probability is >= 0 and <= 100 && !string.IsNullOrWhiteSpace(result.Explanation))
                {
                    return new SpamRequestResult(ResultType.Evaluated, result.Probability, result.Explanation);
                }

                logger.LogWarning("Spam detection returned an invalid score or explanation");
                return new SpamRequestResult(ResultType.Error);
            }
            catch (ClientResultException e)
            {
                logger.LogError("Spam detection request failed with status {Status}", e.Status);
                return new SpamRequestResult(ResultType.Error);
            }
            catch (Exception e)
            {
                logger.LogError("Spam detection failed ({ErrorType})", e.GetType().Name);
                return new SpamRequestResult(ResultType.Error);
            }
        }

        public async Task<ImageAnalysisResult> ExtractImageText(BinaryData jpeg)
        {
            if (jpeg.ToMemory().IsEmpty)
            {
                return new ImageAnalysisResult();
            }

            try
            {
                var imageRecognitionInstructions = await instructions.GetImageAnalyzerInstructions();
                ChatCompletion completion = await imageAnalyzer.CompleteChatAsync(
                [
                    new SystemChatMessage(imageRecognitionInstructions),
                    new UserChatMessage(
                        ChatMessageContentPart.CreateImagePart(jpeg, "image/jpeg"))

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

                return JsonSerializer.Deserialize<ImageAnalysisResult>(imageResponse) ?? new ImageAnalysisResult();
            }
            catch (Exception exception)
            {
                logger.LogWarning("Image analysis failed ({ErrorType}); continuing without image content", exception.GetType().Name);
                return new ImageAnalysisResult();
            }
        }
    }
}
