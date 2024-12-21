namespace TelegramAntiSpamBot.OpenAI
{
    internal class SpamDetectionInstructions
    {
        public async Task<string> GetSpamDetectionInstructions(bool explainDecision)
        {
            var instructions = await File.ReadAllTextAsync("SpamDetectionInstructions.md");
            
            if (explainDecision)
            {
                instructions += "\n\r- Add short explanation of your decision";
            }

            return instructions;
        }

        public async Task<string> GetImageAnalyzerInstructions()
        {
            return await File.ReadAllTextAsync("ImageAnalysis.md");
        }
    }
}
