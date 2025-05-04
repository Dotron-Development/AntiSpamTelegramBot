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
                instructions += "\n\r- Add step-by-step explanation of the results after output in the following format" +
                                "```json\r\n{\r\n  \"Explanation\": <explanation>\r\n}\r\n```";
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
