namespace TelegramAntiSpamBot.Functions;

internal sealed class TelegramReviewAuth
{
    private const int MaxInitDataLength = 16 * 1024;
    private const int MaxLaunchAgeSeconds = 30 * 60;
    private const int LaunchClockSkewSeconds = 30;
    private readonly byte[] launchKey;
    private readonly HashSet<long> moderatorIds;
    private readonly TimeProvider clock;

    public TelegramReviewAuth(IOptions<TelegramBotConfiguration> options, IConfiguration configuration, TimeProvider clock)
    {
        this.clock = clock;
        var botToken = Encoding.UTF8.GetBytes(options.Value.Token);
        launchKey = HMACSHA256.HashData("WebAppData"u8, botToken);

        moderatorIds = ParseModeratorIds(configuration["ReviewTelegram:ModeratorIds"]);
    }

    private static HashSet<long> ParseModeratorIds(string? configuredIds)
    {
        var ids = new HashSet<long>();
        foreach (var value in (configuredIds ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new InvalidOperationException("ReviewTelegram:ModeratorIds must contain positive Telegram user IDs.");

            ids.Add(id);
        }

        return ids;
    }

    public LaunchUser? GetVerifiedLaunchUser(string? initData)
    {
        if (!TryParseLaunchFields(initData, out var fields))
            return null;

        Span<byte> hash = stackalloc byte[32];
        if (!TryExtractHashBytes(fields, hash))
            return null;

        if (!TryGetVerifiedLaunchUserJson(fields, hash, out var userJson))
            return null;

        try
        {
            var user = JsonSerializer.Deserialize<LaunchUser>(userJson);
            return user is { Id: > 0 } ? user : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal sealed record LaunchUser([property: System.Text.Json.Serialization.JsonPropertyName("id")] long Id);

    private static bool TryParseLaunchFields(string? initData,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SortedDictionary<string, string>? fields)
    {
        fields = null;
        if (string.IsNullOrEmpty(initData) || initData.Length > MaxInitDataLength)
            return false;

        var parsedFields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in initData.Split('&'))
        {
            var separator = field.IndexOf('=');
            if (separator <= 0)
                return false;

            var key = WebUtility.UrlDecode(field[..separator]);
            var value = WebUtility.UrlDecode(field[(separator + 1)..]);
            if (key.Length == 0 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') ||
                value.Contains('\n') || value.Contains('\r') || !parsedFields.TryAdd(key, value))
                return false;
        }

        fields = parsedFields;
        return true;
    }

    private static bool TryExtractHashBytes(SortedDictionary<string, string> fields, Span<byte> hash) =>
        fields.Remove("hash", out var hashHex) && hashHex.Length == hash.Length * 2 &&
        Convert.FromHexString(hashHex, hash, out _, out var written) == OperationStatus.Done && written == hash.Length;

    private bool TryGetVerifiedLaunchUserJson(SortedDictionary<string, string> fields, ReadOnlySpan<byte> hash,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? userJson)
    {
        userJson = null;
        var checkString = BuildDataCheckString(fields);
        var expectedHash = HMACSHA256.HashData(launchKey, Encoding.UTF8.GetBytes(checkString));
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, hash) ||
            !fields.TryGetValue("auth_date", out var date) ||
            !long.TryParse(date, NumberStyles.None, CultureInfo.InvariantCulture, out var authDate) ||
            !fields.TryGetValue("user", out var verifiedUserJson))
            return false;

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (authDate < now - MaxLaunchAgeSeconds || authDate > now + LaunchClockSkewSeconds)
            return false;

        userJson = verifiedUserJson;
        return true;
    }

    private static string BuildDataCheckString(SortedDictionary<string, string> fields) =>
        string.Join('\n', fields.Select(field => $"{field.Key}={field.Value}"));

    public bool IsModerator(long userId) => moderatorIds.Contains(userId);
}
