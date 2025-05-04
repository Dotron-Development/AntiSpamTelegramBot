namespace TelegramAntiSpamBot.Persistence
{
    public record ShortcutCheckResult
    {
        public bool? IsSpam { get; init; }
        public bool IsShortCut { get; init; }

        private ShortcutCheckResult(bool? IsSpam, bool IsShortCut)
        {
            this.IsSpam = IsSpam;
            this.IsShortCut = IsShortCut;
        }

        public static ShortcutCheckResult Spam => new(true, true);
        public static ShortcutCheckResult NotSpam => new(false, true);
        public static ShortcutCheckResult NotFound => new(null, false);
    }
}
