namespace TelegramAntiSpamBot.IntegrationTests;

public class TelegramReviewAuthTests
{
    private const string TestBotToken = "fixture-bot-token";
    private const long Moderator = 4500000000000;
    private readonly TestClock clock = new();

    [Fact]
    public void SignedLaunchPreservesLargeUserIdAndChecksTheAllowlist()
    {
        var auth = CreateAuth();
        var moderator = Assert.IsType<TelegramReviewAuth.LaunchUser>(auth.GetVerifiedLaunchUser(SignLaunch(Moderator)));
        Assert.Equal(Moderator, moderator.Id);
        Assert.True(auth.IsModerator(moderator.Id));
        var other = Assert.IsType<TelegramReviewAuth.LaunchUser>(auth.GetVerifiedLaunchUser(SignLaunch(42)));
        Assert.False(auth.IsModerator(other.Id));
        Assert.False(CreateAuth(allowedIds: "").IsModerator(moderator.Id));
    }

    [Fact]
    public void LaunchCannotBeAlteredOrContainDuplicateFields()
    {
        var auth = CreateAuth();
        var launch = SignLaunch(Moderator);
        Assert.Null(auth.GetVerifiedLaunchUser(launch.Replace("A%2BB", "A%2BC")));
        Assert.Null(auth.GetVerifiedLaunchUser(launch + "&%75ser=%7B%7D"));
        Assert.Null(CreateAuth(botToken: "another-bot-token").GetVerifiedLaunchUser(launch));
    }

    [Theory]
    [InlineData(-1801)]
    [InlineData(31)]
    public void LaunchOutsideTheAcceptedTimeWindowIsRejected(int offset) =>
        Assert.Null(CreateAuth().GetVerifiedLaunchUser(SignLaunch(Moderator, offset)));

    [Fact]
    public void GuardChecksTheSameLaunchAgainAfterItExpires()
    {
        var guard = new ReviewAuthorizationMiddleware(CreateAuth());
        var authorization = $"TMA {SignLaunch(Moderator, -1800)}";

        Assert.Null(guard.CheckAuthorization([authorization]));
        clock.Now = clock.Now.AddSeconds(1);
        Assert.Equal(HttpStatusCode.Unauthorized, guard.CheckAuthorization([authorization]));
    }

    [Fact]
    public void GuardRejectsMissingOldOrAmbiguousCredentialsAndChecksModeratorAccess()
    {
        var guard = new ReviewAuthorizationMiddleware(CreateAuth());
        var launch = SignLaunch(Moderator);

        Assert.Equal(HttpStatusCode.Unauthorized, guard.CheckAuthorization([]));
        Assert.Equal(HttpStatusCode.Unauthorized, guard.CheckAuthorization([$"Bearer {launch}"]));
        Assert.Equal(HttpStatusCode.Unauthorized, guard.CheckAuthorization([$"tma {launch}", $"tma {launch}"]));
        Assert.Equal(HttpStatusCode.Forbidden, guard.CheckAuthorization([$"tma {SignLaunch(42)}"]));
    }

    private TelegramReviewAuth CreateAuth(string allowedIds = "4500000000000", string botToken = TestBotToken) =>
        new(Options.Create(new TelegramBotConfiguration
            {
                Token = botToken, SecretHeader = "fixture-webhook-secret", BotName = "fixture", DebugAiResponse = false
            }),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReviewTelegram:ModeratorIds"] = allowedIds
            }).Build(), clock);

    private string SignLaunch(long userId, int offset = 0)
    {
        var date = clock.Now.AddSeconds(offset).ToUnixTimeSeconds();
        var user = $$"""{"id":{{userId}},"first_name":"A+B & C"}""";
        const string signature = "example-signature";
        var data = $"auth_date={date}\nsignature={signature}\nuser={user}";
        var key = HMACSHA256.HashData("WebAppData"u8, Encoding.UTF8.GetBytes(TestBotToken));
        var hash = Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data)));
        return $"user={Uri.EscapeDataString(user)}&hash={hash}&signature={signature}&auth_date={date}";
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
