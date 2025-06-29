namespace TelegramAntiSpamBot.OpenAI.Instructions
{
    internal class SpamDetectionInstructions
    {
        private static readonly Assembly Assembly = typeof(SpamDetectionInstructions).Assembly;

        public async Task<string> GetSpamDetectionInstructions(bool explainDecision)
        {
            var instructions = await FindResource("SpamDetectionInstructions.md");

            if (explainDecision)
            {
                instructions += "\n\r- Add step-by-step but short explanation of the results after output in the following format to the output json with probability" +
                                " \"Explanation\": <explanation>";
            }

            return instructions;
        }

        public async Task<string> GetImageAnalyzerInstructions()
        {
            var instructions = await FindResource("ImageAnalysis.md");
            return instructions;
        }

        private static async Task<string> FindResource(string name)
        {
            var resource = Assembly.GetManifestResourceNames().First(x => x.EndsWith(name));
            var stream = Assembly.GetManifestResourceStream(resource);
            return await ConvertStreamToString(stream!);
        }

        public static async Task<string> ConvertStreamToString(Stream stream)
        {
            stream.Seek(0, SeekOrigin.Begin);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
    }
}
