namespace TelegramAntiSpamBot.OpenAI.Instructions
{
    internal class SpamDetectionInstructions
    {
        private static readonly Assembly Assembly = typeof(SpamDetectionInstructions).Assembly;

        public Task<string> GetSpamDetectionInstructions() => FindResource("SpamDetectionInstructions.md");

        public async Task<string> GetImageAnalyzerInstructions()
        {
            var instructions = await FindResource("ImageAnalysis.md");
            return instructions;
        }

        public Task<string> GetChronicleInstructions() => FindResource("ChronicleInstructions.md");

        public Task<string> GetMentionInstructions() => FindResource("MentionInstructions.md");

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
