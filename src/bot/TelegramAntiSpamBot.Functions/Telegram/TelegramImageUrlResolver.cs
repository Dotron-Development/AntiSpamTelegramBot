namespace TelegramAntiSpamBot.Functions
{
    public class TelegramImageUrlResolver(string botToken)
    {
        public string GetImageUrl(string imagePath)
        {
            return $"https://api.telegram.org/file/bot{botToken}/{imagePath}";
        }
    }
}
