
namespace TelegramAntiSpamBot.IntegrationTests;

public class ReviewFunctionsTests
{
    [Fact]
    public async Task ChroniclePreviewUsesConfiguredCooldownAndDoesNotPostToTelegram()
    {
        using var fixture = new Fixture(commandCooldown: TimeSpan.FromMinutes(2));
        var response = await fixture.HistoryFunctions.PreviewChronicle(fixture.Request(), -100);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((-100L, "/chronicle", TimeSpan.FromMinutes(2)), fixture.PreviewCall);
        Assert.Equal(1, fixture.PreviewGenerations);
        Assert.Empty(fixture.Telegram.Calls);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        Assert.Equal("Preview summary", document.RootElement.GetProperty("summary").GetString());
        Assert.False(document.RootElement.GetProperty("skipped").GetBoolean());
    }

    [Fact]
    public async Task ChroniclePreviewOnCooldownReturnsSkippedWithoutCallingAiOrTelegram()
    {
        using var fixture = new Fixture { AllowCommandCall = false };
        var response = await fixture.HistoryFunctions.PreviewChronicle(fixture.Request(), -100);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, fixture.PreviewGenerations);
        Assert.Empty(fixture.Telegram.Calls);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        Assert.True(document.RootElement.GetProperty("skipped").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("summary").ValueKind);
    }

    [Fact]
    public async Task ChroniclePreviewRejectsInvalidChatAndReportsAiFailure()
    {
        using var fixture = new Fixture();
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.HistoryFunctions.PreviewChronicle(fixture.Request(), 0)).StatusCode);
        Assert.Null(fixture.PreviewCall);
        fixture.PreviewFails = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await fixture.HistoryFunctions.PreviewChronicle(fixture.Request(), -100)).StatusCode);
        Assert.Empty(fixture.Telegram.Calls);
    }

    [Fact]
    public void ChroniclePreviewUsesAuthenticatedPostRoute()
    {
        var trigger = typeof(HistoryReviewFunctions).GetMethod(nameof(HistoryReviewFunctions.PreviewChronicle))!
            .GetParameters()[0].GetCustomAttribute<HttpTriggerAttribute>();
        Assert.Equal(AuthorizationLevel.Anonymous, trigger!.AuthLevel);
        Assert.Equal(new[] { "post" }, trigger.Methods);
        Assert.Equal("review/chronicle/{chatId:long}", trigger.Route);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryViewsPassChatAndPaginationAndReturnRecords(bool calls)
    {
        using var fixture = new Fixture();
        fixture.History.Add(new(-100, 7, DateTime.UtcNow.Ticks, new(new(42, "Alice", "alice"), null, "Original text")));
        var request = fixture.Request(query: "?chatId=-100&pageSize=25&continuationToken=opaque");
        var response = calls ? await fixture.HistoryFunctions.GetCommandCalls(request) : await fixture.HistoryFunctions.GetHistory(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((-100L, 25, "opaque"), fixture.HistoryQuery);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        Assert.Equal("next-record", document.RootElement.GetProperty("continuationToken").GetString());
        var row = document.RootElement.GetProperty(calls ? "calls" : "messages")[0];
        Assert.Equal(-100, row.GetProperty("chatId").GetInt64());
        if (calls)
            Assert.Equal("/chronicle", row.GetProperty("command").GetString());
        else
            Assert.Equal("Original text", row.GetProperty("content").GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("?chatId=0")]
    [InlineData("?chatId=bad")]
    [InlineData("?chatId=9223372036854775808")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=1001")]
    [InlineData("?pageSize=bad")]
    public async Task HistoryViewsRejectInvalidQueriesWithoutReadingTables(string query)
    {
        using var fixture = new Fixture();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await fixture.HistoryFunctions.GetHistory(fixture.Request(query: query))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await fixture.HistoryFunctions.GetCommandCalls(fixture.Request(query: query))).StatusCode);
        Assert.Null(fixture.HistoryQuery);
    }

    [Theory]
    [InlineData(nameof(HistoryReviewFunctions.GetHistory))]
    [InlineData(nameof(HistoryReviewFunctions.GetCommandCalls))]
    public void HistoryViewsUsePlatformAuthenticatedReadOnlyEndpoints(string methodName)
    {
        var trigger = typeof(HistoryReviewFunctions).GetMethod(methodName)!.GetParameters()[0]
            .GetCustomAttribute<HttpTriggerAttribute>();
        Assert.Equal(AuthorizationLevel.Anonymous, trigger!.AuthLevel);
        Assert.Equal(new[] { "get" }, trigger.Methods);
        Assert.StartsWith("review/", trigger.Route);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReviewUpdatesCacheAndRemovesEntryWithoutLiftingRestrictions(bool isSpam)
    {
        using var fixture = new Fixture();
        var response = await fixture.Functions.Review(fixture.Request($"{{\"isSpam\":{isSpam.ToString().ToLowerInvariant()}}}"), -100, 7);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(("HASH", isSpam), Assert.Single(fixture.HashDecisions));
        Assert.True(fixture.Deleted);
        Assert.Equal(isSpam ? 0 : 1, fixture.Telegram.Calls.Count);
        Assert.DoesNotContain(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
        if (!isSpam)
            Assert.Equal("from: Alice @alice\nOriginal caption\nRecognized text", fixture.Telegram.Calls[0].Body.GetProperty("text").GetString());
    }

    [Fact]
    public async Task NotSpamReplyTargetsOriginalRepliedToMessage()
    {
        using var fixture = new Fixture();
        fixture.Message = fixture.Message! with
        {
            ReplyTo = new ReviewMessageReplyTo(5, new ReviewMessageFrom("Bob", "bob"), "Question")
        };

        await fixture.Functions.Review(fixture.Request("{\"isSpam\":false}"), -100, 7);

        var sent = Assert.Single(fixture.Telegram.Calls, call => call.Method == "sendmessage");
        var replyParameters = sent.Body.GetProperty("reply_parameters");
        Assert.Equal(5, replyParameters.GetProperty("message_id").GetInt32());
        Assert.True(replyParameters.GetProperty("allow_sending_without_reply").GetBoolean());
    }

    [Fact]
    public async Task NotSpamRepublishOmitsMissingNick()
    {
        using var fixture = new Fixture
        {
            Message =             new ReviewMessage(-100, 7, 42, new ReviewMessageFrom("Alice", null), null,
                "Original caption", "Recognized text", "HASH", DateTime.UtcNow.Ticks)
        };

        await fixture.Functions.Review(fixture.Request("{\"isSpam\":false}"), -100, 7);

        Assert.Equal("from: Alice\nOriginal caption\nRecognized text",
            fixture.Telegram.Calls[0].Body.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(179, true)]
    [InlineData(181, false)]
    [InlineData(10000, false)]
    public async Task NotSpamOnlyRepublishesMessagesWithinRestrictionTimeout(int ageMinutes, bool shouldPublish)
    {
        using var fixture = new Fixture();
        fixture.Message = fixture.Message! with
        {
            MessageDateTicks = DateTime.UtcNow.AddMinutes(-ageMinutes).Ticks
        };

        var response = await fixture.Functions.Review(fixture.Request("{\"isSpam\":false}"), -100, 7);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(("HASH", false), Assert.Single(fixture.HashDecisions));
        Assert.True(fixture.Deleted);
        Assert.Equal(shouldPublish ? 1 : 0, fixture.Telegram.Calls.Count);
        Assert.DoesNotContain(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
    }

    [Fact]
    public async Task ReviewPreservesLongUnicodeTextAcrossTelegramMessages()
    {
        using var fixture = new Fixture();
        var original = new string('a', 4096 - "from: Alice @alice\n".Length - 1)
            + char.ConvertFromUtf32(0x1F600) + new string('b', 4200);
        fixture.Message = fixture.Message! with
        {
            Message = original,
            ImageText = string.Empty,
            ReplyTo = new ReviewMessageReplyTo(5, null, "Original question")
        };

        await fixture.Functions.Review(fixture.Request("{\"isSpam\":false}"), -100, 7);

        var chunks = fixture.Telegram.Calls.Select(call => call.Body.GetProperty("text").GetString()!).ToArray();
        Assert.Equal("from: Alice @alice\n" + original, string.Concat(chunks));
        Assert.Equal(new[] { 5, 99, 100 }, fixture.Telegram.Calls.Select(call =>
            call.Body.GetProperty("reply_parameters").GetProperty("message_id").GetInt32()));
        Assert.All(chunks, chunk =>
        {
            Assert.InRange(chunk.Length, 1, 4096);
            Assert.False(char.IsHighSurrogate(chunk[^1]));
            Assert.False(char.IsLowSurrogate(chunk[0]));
        });
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(31, false)]
    public async Task NotSpamUsesConfiguredRestrictionTimeout(int ageMinutes, bool shouldPublish)
    {
        using var fixture = new Fixture(TimeSpan.FromMinutes(30));
        fixture.Message = fixture.Message! with
        {
            MessageDateTicks = DateTime.UtcNow.AddMinutes(-ageMinutes).Ticks
        };

        var response = await fixture.Functions.Review(fixture.Request("{\"isSpam\":false}"), -100, 7);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(shouldPublish ? 1 : 0, fixture.Telegram.Calls.Count);
        Assert.True(fixture.Deleted);
        Assert.Equal(("HASH", false), Assert.Single(fixture.HashDecisions));
    }

    [Fact]
    public async Task WebhookUsesConfiguredRestrictionTimeout()
    {
        using var fixture = new Fixture(TimeSpan.FromMinutes(30));
        var earliestUntil = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();

        await fixture.Webhook.Run(fixture.Request(PhotoUpdate));

        var restriction = Assert.Single(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
        Assert.InRange(restriction.Body.GetProperty("until_date").GetInt64(),
            earliestUntil, DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task EmptyContentDoesNotPublishOrCacheAnEmptyHash()
    {
        using var fixture = new Fixture();
        fixture.Message = fixture.Message! with { Message = "", ImageText = "", ContentHash = "" };

        var response = await fixture.Functions.Review(fixture.Request("{\"isSpam\":false}"), -100, 7);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(fixture.HashDecisions);
        Assert.Empty(fixture.Telegram.Calls);
        Assert.True(fixture.Deleted);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"isSpam\":null}")]
    [InlineData("{\"isSpam\":\"false\"}")]
    [InlineData("not json")]
    public async Task InvalidDecisionDoesNotModifyAnything(string body)
    {
        using var fixture = new Fixture();

        var response = await fixture.Functions.Review(fixture.Request(body), -100, 7);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fixture.HashDecisions);
        Assert.Empty(fixture.Telegram.Calls);
        Assert.False(fixture.Deleted);
    }

    [Fact]
    public async Task MissingMessageReturnsNotFound()
    {
        using var fixture = new Fixture();
        fixture.Message = null;

        var response = await fixture.Functions.Review(fixture.Request("{\"isSpam\":true}"), -100, 7);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(fixture.HashDecisions);
        Assert.False(fixture.Deleted);
    }

    [Fact]
    public async Task ListPassesChatAndPaginationToRepository()
    {
        using var fixture = new Fixture();

        var response = await fixture.Functions.GetMessages(fixture.Request(query: "?chatId=-100&pageSize=12&continuationToken=opaque"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((-100L, 12, "opaque"), fixture.Query);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        Assert.Equal(7, document.RootElement.GetProperty("messages")[0].GetProperty("messageId").GetInt32());
        Assert.Equal("next", document.RootElement.GetProperty("continuationToken").GetString());
    }

    [Theory]
    [InlineData("", 100, null)]
    [InlineData("?pageSize=12&continuationToken=opaque", 12, "opaque")]
    public async Task ListWithoutChatPassesNullAndPaginationToRepository(string query, int pageSize, string? token)
    {
        using var fixture = new Fixture();

        var response = await fixture.Functions.GetMessages(fixture.Request(query: query));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(((long?)null, pageSize, token), fixture.Query);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        Assert.Equal(7, document.RootElement.GetProperty("messages")[0].GetProperty("messageId").GetInt32());
        Assert.Equal("next", document.RootElement.GetProperty("continuationToken").GetString());
    }

    [Theory]
    [InlineData("?chatId=0")]
    [InlineData("?chatId=")]
    [InlineData("?chatId=invalid")]
    [InlineData("?chatId=9223372036854775808")]
    [InlineData("?chatId=-100&pageSize=0")]
    [InlineData("?chatId=-100&pageSize=1001")]
    [InlineData("?pageSize=invalid")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=1001")]
    public async Task ListRejectsInvalidSuppliedChatOrPageSize(string query)
    {
        using var fixture = new Fixture();

        var response = await fixture.Functions.GetMessages(fixture.Request(query: query));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(fixture.Query);
    }

    [Fact]
    public async Task RemoveRestrictionsRestoresChatDefaultsIndependently()
    {
        using var fixture = new Fixture();

        var response = await fixture.Functions.RemoveRestrictions(fixture.Request(), -100, 42);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var restriction = Assert.Single(fixture.Telegram.Calls, call => call.Method == "restrictchatmember").Body;
        Assert.Equal(42, restriction.GetProperty("user_id").GetInt64());
        Assert.True(restriction.GetProperty("permissions").GetProperty("can_send_messages").GetBoolean());
        Assert.False(fixture.Deleted);
        Assert.Empty(fixture.HashDecisions);
    }

    [Theory]
    [InlineData(nameof(ReviewFunctions.GetChats))]
    [InlineData(nameof(ReviewFunctions.GetMessages))]
    [InlineData(nameof(ReviewFunctions.Review))]
    [InlineData(nameof(ReviewFunctions.RemoveRestrictions))]
    public void AllReviewEndpointsDelegateAuthenticationToEasyAuth(string methodName)
    {
        var trigger = typeof(ReviewFunctions).GetMethod(methodName)!.GetParameters()[0]
            .GetCustomAttribute<HttpTriggerAttribute>();
        Assert.Equal(AuthorizationLevel.Anonymous, trigger!.AuthLevel);
    }

    [Fact]
    public async Task AiSpamQueuesOriginalCaptionAndOcrWithoutCachingTrue()
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new SpamRequestResult(ResultType.Evaluated, 99);

        var response = await fixture.Webhook.Run(fixture.Request(PhotoUpdate));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var message = Assert.IsType<ReviewMessage>(fixture.SavedMessage);
        Assert.Equal(-100, message.ChatId);
        Assert.Equal(7, message.MessageId);
        Assert.Equal(42, message.UserId);
        Assert.Equal(new ReviewMessageFrom("Alice Smith", "alice"), message.From);
        Assert.Equal("Original Caption 123!", message.Message);
        Assert.Equal("OCR \"quoted\"\nsecond line", message.ImageText);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime.Ticks, message.MessageDateTicks);
        Assert.Contains(message.Message, fixture.Detector.ClassifiedText);
        Assert.Contains(message.ImageText, fixture.Detector.ClassifiedText);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.Unicode.GetBytes(
            MessageContentSanitizer.Sanitize(fixture.Detector.ClassifiedText!)))), message.ContentHash);
        Assert.Empty(fixture.HashDecisions);
        Assert.Contains(fixture.Telegram.Calls, call => call.Method == "deletemessage");
        Assert.Contains(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
    }

    [Fact]
    public async Task AiSafeCachesOnlyFalseAndDoesNotModerate()
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new SpamRequestResult(ResultType.Evaluated, 10);

        var response = await fixture.Webhook.Run(fixture.Request(PhotoUpdate));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(Assert.Single(fixture.HashDecisions).IsSpam);
        Assert.Null(fixture.SavedMessage);
        Assert.DoesNotContain(fixture.Telegram.Calls, call => call.Method == "deletemessage");
    }

    [Fact]
    public async Task ConfirmedSpamHashModeratesWithoutAnotherReviewOrAiCall()
    {
        using var fixture = new Fixture();
        fixture.Shortcut = ShortcutCheckResult.Spam;

        await fixture.Webhook.Run(fixture.Request(PhotoUpdate));

        Assert.Null(fixture.Detector.ClassifiedText);
        Assert.Null(fixture.SavedMessage);
        Assert.Empty(fixture.HashDecisions);
        Assert.Contains(fixture.Telegram.Calls, call => call.Method == "deletemessage");
        Assert.Contains(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
    }

    [Fact]
    public async Task AiErrorKeepsExistingFailureResponseWithoutDecisions()
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new SpamRequestResult(ResultType.Error);

        var response = await fixture.Webhook.Run(fixture.Request(PhotoUpdate));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Null(fixture.SavedMessage);
        Assert.Empty(fixture.HashDecisions);
        Assert.DoesNotContain(fixture.Telegram.Calls, call => call.Method == "deletemessage");
    }

    [Fact]
    public async Task WebhookRegistersChatBeforeModeration()
    {
        using var fixture = new Fixture();
        await fixture.Webhook.Run(fixture.Request(PhotoUpdate));
        var chat = Assert.Single(fixture.SavedChats);
        Assert.Equal(-100, chat.ChatId);
        Assert.Equal("Test", chat.Name);
        Assert.InRange(chat.LastSeenUtc, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ChatCatalogFailureDoesNotPreventModeration()
    {
        using var fixture = new Fixture();
        fixture.ChatSaveFails = true;
        await fixture.Webhook.Run(fixture.Request(PhotoUpdate));
        Assert.NotNull(fixture.SavedMessage);
        Assert.Contains(fixture.Telegram.Calls, call => call.Method == "deletemessage");
    }

    [Fact]
    public async Task ChatListPassesPaginationAndReturnsNames()
    {
        using var fixture = new Fixture();
        var response = await fixture.Functions.GetChats(fixture.Request(query: "?pageSize=12&continuationToken=opaque"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((12, "opaque"), fixture.ChatQuery);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        Assert.Equal(-100, document.RootElement.GetProperty("chats")[0].GetProperty("chatId").GetInt64());
        Assert.Equal("Test", document.RootElement.GetProperty("chats")[0].GetProperty("name").GetString());
        Assert.Equal("next-chat", document.RootElement.GetProperty("continuationToken").GetString());
    }

    [Theory]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=1001")]
    [InlineData("?pageSize=invalid")]
    public async Task ChatListRejectsInvalidPageSize(string query)
    {
        using var fixture = new Fixture();
        var response = await fixture.Functions.GetChats(fixture.Request(query: query));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(fixture.ChatQuery);
    }

    private const string PhotoUpdate = """
        {"update_id":1,"message":{"message_id":7,"date":1700000000,
        "from":{"id":42,"is_bot":false,"first_name":"Alice","last_name":"Smith","username":"alice"},
        "chat":{"id":-100,"type":"supergroup","title":"Test"},
        "caption":"Original Caption 123!",
        "photo":[{"file_id":"photo","file_unique_id":"unique","width":100,"height":100}]}}
        """;

    [Fact]
    public async Task WebhookSavesReplyContextForReviewQueue()
    {
        using var fixture = new Fixture();
        const string update = """
            {"update_id":3,"message":{"message_id":9,"date":1700000002,
            "from":{"id":42,"is_bot":false,"first_name":"Alice","username":"alice"},
            "chat":{"id":-100,"type":"supergroup","title":"Test"},"text":"This is a sufficiently long answer",
            "reply_to_message":{"message_id":4,"date":1700000001,
            "from":{"id":99,"is_bot":false,"first_name":"Bob","username":"bob"},
            "chat":{"id":-100,"type":"supergroup","title":"Test"},"text":"Question"}}}
            """;

        var response = await fixture.Webhook.Run(fixture.Request(update));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new ReviewMessageReplyTo(4, new ReviewMessageFrom("Bob", "bob"), "Question"),
            Assert.IsType<ReviewMessage>(fixture.SavedMessage).ReplyTo);
    }

    [Fact]
    public async Task WebhookSavesOriginalMessageToHistory()
    {
        using var fixture = new Fixture();

        await fixture.Webhook.Run(fixture.Request(PhotoUpdate));

        var message = Assert.Single(fixture.History);
        Assert.Equal(-100, message.ChatId);
        Assert.Equal(7, message.MessageId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime.Ticks, message.DateTimeTicks);
        Assert.Equal("Original Caption 123!", message.Content.Text);
        Assert.Equal(new HistoryMessageAuthor(42, "Alice Smith", "alice"), message.Content.From);
    }

    [Fact]
    public async Task WebhookSavesChannelPostsAndReplyContextWithoutUserModeration()
    {
        using var fixture = new Fixture();
        const string update = """
            {"update_id":2,"channel_post":{"message_id":8,"date":1700000001,
            "chat":{"id":-200,"type":"channel","title":"News"},
            "sender_chat":{"id":-200,"type":"channel","title":"News"},"text":"New post",
            "reply_to_message":{"message_id":7,"date":1700000000,
            "chat":{"id":-200,"type":"channel","title":"News"},
            "from":{"id":42,"is_bot":false,"first_name":"Alice"},"text":"Previous post"}}}
            """;

        var response = await fixture.Webhook.Run(fixture.Request(update));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var message = Assert.Single(fixture.History);
        Assert.Equal(-200, message.ChatId);
        Assert.Equal(new HistoryMessageAuthor(-200, "News", null), message.Content.From);
        Assert.Equal(new HistoryMessageReply(7, new(42, "Alice", null), "Previous post"), message.Content.ReplyTo);
        Assert.Empty(fixture.Telegram.Calls);
        Assert.Single(fixture.SavedChats);
    }

    [Fact]
    public async Task HourlyCleanupCallsHistoryRepository()
    {
        using var fixture = new Fixture();
        var function = new MessageHistoryCleanupFunctions(fixture.HistoryRepository,
            NullLogger<MessageHistoryCleanupFunctions>.Instance);

        await function.Run(null!, CancellationToken.None);

        Assert.True(fixture.HistoryTrimmed);
        var timer = typeof(MessageHistoryCleanupFunctions).GetMethod(nameof(MessageHistoryCleanupFunctions.Run))!
            .GetParameters()[0].GetCustomAttribute<TimerTriggerAttribute>();
        Assert.Equal("0 0 * * * *", timer!.Schedule);
        Assert.True(timer.UseMonitor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WebhookProcessesMessageWhileWritesRunAndAwaitsBothBeforeReturning(bool shortcut)
    {
        using var fixture = new Fixture();
        var historyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.HistorySaveTask = historyGate.Task;
        fixture.ChatSaveTask = chatGate.Task;
        if (shortcut)
            fixture.Shortcut = ShortcutCheckResult.NotSpam;

        var run = fixture.Webhook.Run(fixture.Request(PhotoUpdate));
        try
        {
            await fixture.SpamChecked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(fixture.History);
            Assert.Single(fixture.SavedChats);
            Assert.False(run.IsCompleted);
            historyGate.SetResult();
            Assert.False(run.IsCompleted);
        }
        finally
        {
            historyGate.TrySetResult();
            chatGate.TrySetResult();
        }

        Assert.Equal(HttpStatusCode.NoContent, (await run).StatusCode);
    }

    [Fact]
    public async Task HistoryFailureReturnsServerErrorAfterProcessingMessage()
    {
        using var fixture = new Fixture();
        fixture.HistorySaveTask = Task.FromException(new InvalidOperationException("Storage unavailable"));

        var response = await fixture.Webhook.Run(fixture.Request(PhotoUpdate));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotNull(fixture.Detector.ClassifiedText);
        Assert.Contains(fixture.Telegram.Calls, call => call.Method == "deletemessage");
        Assert.Single(fixture.SavedChats);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider services = new ServiceCollection()
            .AddSingleton(Options.Create(new WorkerOptions
            {
                Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            })).BuildServiceProvider();
        private readonly HttpClient httpClient;
        public TelegramHandler Telegram { get; } = new();
        public ReviewMessage? Message { get; set; } = new(-100, 7, 42, new ReviewMessageFrom("Alice", "alice"), null, "Original caption", "Recognized text", "HASH", DateTime.UtcNow.Ticks);
        public List<(string Hash, bool IsSpam)> HashDecisions { get; } = [];
        public bool Deleted { get; private set; }
        public List<HistoryMessage> History { get; } = [];
        public IMessageHistoryRepository HistoryRepository { get; }
        public HistoryReviewFunctions HistoryFunctions { get; }
        public (long? ChatId, int PageSize, string? Token)? HistoryQuery { get; private set; }
        public bool AllowCommandCall { get; set; } = true;
        public bool PreviewFails { get; set; }
        public int PreviewGenerations { get; private set; }
        public (long ChatId, string Command, TimeSpan Cooldown)? PreviewCall { get; private set; }
        public bool HistoryTrimmed { get; private set; }
        public Task HistorySaveTask { get; set; } = Task.CompletedTask;
        public Task ChatSaveTask { get; set; } = Task.CompletedTask;
        public TaskCompletionSource SpamChecked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public (long? ChatId, int PageSize, string? Token)? Query { get; private set; }
        public ReviewFunctions Functions { get; }
        public AntiSpamBotWebHook Webhook { get; }
        public DetectorStub Detector { get; } = new();
        public ShortcutCheckResult Shortcut { get; set; } = ShortcutCheckResult.NotFound;
        public ReviewMessage? SavedMessage { get; private set; }
        public List<ReviewChat> SavedChats { get; } = [];
        public bool ChatSaveFails { get; set; }
        public (int PageSize, string? Token)? ChatQuery { get; private set; }

        public Fixture(TimeSpan? spamRestrictionTimeout = null, TimeSpan? commandCooldown = null)
        {
            object? HandleRepositoryCall(MethodInfo method, object?[] arguments)
            {
                switch (method.Name)
                {
                    case nameof(ICommandCallsRepository.TryAcquireAsync):
                        PreviewCall = ((long)arguments[0]!, (string)arguments[1]!, (TimeSpan)arguments[3]!);
                        return Task.FromResult(AllowCommandCall);
                    case nameof(IChronicleService.GenerateAsync):
                        Assert.Equal(-100L, arguments[0]);
                        Assert.Null(arguments[1]);
                        PreviewGenerations++;
                        return PreviewFails ? Task.FromException<string>(new InvalidOperationException("AI unavailable"))
                            : Task.FromResult("Preview summary");
                    case nameof(ICommandCallsRepository.GetPageAsync):
                        HistoryQuery = ((long?)arguments[0], (int)arguments[1]!, (string?)arguments[2]);
                        if (method.DeclaringType == typeof(ICommandCallsRepository))
                            return Task.FromResult(new CommandCallPage([new(-100, "/chronicle", DateTimeOffset.UtcNow)], "next-record"));
                        return Task.FromResult(new HistoryMessagePage(History.ToArray(), "next-record"));
                    case nameof(IMessageHistoryRepository.SaveAsync):
                        History.Add((HistoryMessage)arguments[0]!);
                        return HistorySaveTask;
                    case nameof(IMessageHistoryRepository.TrimAsync):
                        HistoryTrimmed = true;
                        return Task.FromResult(10);
                    case nameof(IChatsRepository.SaveChatAsync):
                        if (ChatSaveFails) return Task.FromException(new InvalidOperationException("Storage unavailable"));
                        SavedChats.Add((ReviewChat)arguments[0]!);
                        return ChatSaveTask;
                    case nameof(IChatsRepository.GetReviewChatsAsync):
                        ChatQuery = ((int)arguments[0]!, (string?)arguments[1]);
                        return Task.FromResult(new ReviewChatPage([new ReviewChat(-100, "Test", DateTimeOffset.UtcNow)], "next-chat"));
                    case nameof(IMessageCountRepository.GetUserMessageCount):
                        return Task.FromResult(0);
                    case nameof(IMessageCountRepository.IncreaseMessageCount):
                    case nameof(ISpamStatsRepository.UpdateChannelSpamStats):
                        return Task.CompletedTask;
                    case nameof(ISpamHashRepository.IsSpam):
                        SpamChecked.TrySetResult();
                        return Task.FromResult(Shortcut);
                    case nameof(IProbablySpamRepository.SaveReviewMessageAsync):
                        SavedMessage = (ReviewMessage)arguments[0]!;
                        return Task.CompletedTask;
                    case nameof(IProbablySpamRepository.GetReviewMessageAsync):
                        Assert.Equal(-100L, arguments[0]);
                        Assert.Equal(7, arguments[1]);
                        return Task.FromResult(Message);
                    case nameof(IProbablySpamRepository.GetReviewMessagesAsync):
                        Query = ((long?)arguments[0], (int)arguments[1]!, (string?)arguments[2]);
                        return Task.FromResult(new ReviewMessagePage([Message!], "next"));
                    case nameof(ISpamHashRepository.SaveMessageHash):
                        HashDecisions.Add(((string)arguments[0]!, (bool)arguments[1]!));
                        return Task.CompletedTask;
                    case nameof(IProbablySpamRepository.DeleteReviewMessageAsync):
                        Deleted = true;
                        return Task.CompletedTask;
                    default:
                        throw new InvalidOperationException(method.Name);
                }
            }
            T CreateRepository<T>() where T : class
            {
                var repository = DispatchProxy.Create<T, RepositoryProxy>();
                ((RepositoryProxy)(object)repository).Handler = HandleRepositoryCall;
                return repository;
            }
            var chatsRepository = CreateRepository<IChatsRepository>();
            HistoryRepository = CreateRepository<IMessageHistoryRepository>();
            var probablySpamRepository = CreateRepository<IProbablySpamRepository>();
            var messageCountRepository = CreateRepository<IMessageCountRepository>();
            var spamHashRepository = CreateRepository<ISpamHashRepository>();
            var spamStatsRepository = CreateRepository<ISpamStatsRepository>();
            httpClient = new HttpClient(Telegram);
            const string token = "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk";
            var bot = new TelegramBotClient(token, httpClient);
            var botConfig = Options.Create(new TelegramBotConfiguration
            {
                Token = token,
                SecretHeader = "test",
                BotName = "@TestBot",
                DebugAiResponse = false,
                SpamRestrictionTimeout = spamRestrictionTimeout ?? TimeSpan.FromHours(3),
                CommandCooldown = commandCooldown ?? TimeSpan.FromMinutes(5)
            });
            Functions = new ReviewFunctions(chatsRepository, probablySpamRepository, spamHashRepository, bot, botConfig);
            HistoryFunctions = new HistoryReviewFunctions(HistoryRepository, CreateRepository<ICommandCallsRepository>(),
                CreateRepository<IChronicleService>(), botConfig, NullLogger<HistoryReviewFunctions>.Instance);
            Webhook = new AntiSpamBotWebHook(NullLogger<AntiSpamBotWebHook>.Instance,
                Detector, chatsRepository, HistoryRepository, probablySpamRepository, messageCountRepository, spamHashRepository,
                spamStatsRepository, bot, new TelegramImageUrlResolver(token), new CommandStub(),
                botConfig);
        }

        public HttpRequestData Request(string body = "", string query = "") =>
            new TestRequest(new TestContext { InstanceServices = services }, body, query);

        public void Dispose()
        {
            httpClient.Dispose();
            services.Dispose();
        }
    }

    public class RepositoryProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args!);
    }

    private sealed class TelegramHandler : HttpMessageHandler
    {
        public List<(string Method, JsonElement Body)> Calls { get; } = [];
        private int nextMessageId = 99;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var method = request.RequestUri!.Segments[^1].ToLowerInvariant();
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Calls.Add((method, document.RootElement.Clone()));
            var result = method switch
            {
                "restrictchatmember" or "deletemessage" => "true",
                "getfile" => "{\"file_id\":\"photo\",\"file_unique_id\":\"unique\",\"file_path\":\"photos/test.jpg\"}",
                "getchat" => "{\"id\":-100,\"type\":\"supergroup\",\"title\":\"Test\",\"permissions\":{\"can_send_messages\":true}}",
                _ => $"{{\"message_id\":{nextMessageId++},\"date\":0,\"chat\":{{\"id\":-100,\"type\":\"supergroup\"}}}}"
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"ok\":true,\"result\":{result}}}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class DetectorStub : ISpamDetectionService
    {
        public SpamRequestResult Result { get; set; } = new(ResultType.Evaluated, 99);
        public string? ClassifiedText { get; private set; }
        public Task<string> ExtractImageText(string imageUrl) => Task.FromResult("OCR \"quoted\"\nsecond line");
        public Task<SpamRequestResult> IsSpamAsync(string userMessage, int userMessagesCount, bool explainDecision = false)
        {
            ClassifiedText = userMessage;
            return Task.FromResult(Result);
        }
    }

    private sealed class CommandStub : ICommandProcessor
    {
        public bool IsCommand(string? message) => false;
        public Task ProcessCommand(Telegram.Bot.Types.Update update) => Task.CompletedTask;
    }

    private sealed class TestContext : FunctionContext
    {
        public override string InvocationId => "test";
        public override string FunctionId => "test";
        public override TraceContext TraceContext => throw new NotSupportedException();
        public override BindingContext BindingContext => throw new NotSupportedException();
        public override RetryContext RetryContext => throw new NotSupportedException();
        public override IServiceProvider InstanceServices { get; set; } = null!;
        public override FunctionDefinition FunctionDefinition => throw new NotSupportedException();
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features => throw new NotSupportedException();
    }

    private sealed class TestRequest(FunctionContext context, string body, string query) : HttpRequestData(context)
    {
        public override Stream Body { get; } = new MemoryStream(Encoding.UTF8.GetBytes(body));
        public override HttpHeadersCollection Headers { get; } = new();
        public override IReadOnlyCollection<IHttpCookie> Cookies => [];
        public override Uri Url { get; } = new("https://localhost/api/review/messages" + query);
        public override IEnumerable<ClaimsIdentity> Identities => [];
        public override string Method => "POST";
        public override HttpResponseData CreateResponse() => new TestResponse(FunctionContext);
    }

    private sealed class TestResponse(FunctionContext context) : HttpResponseData(context)
    {
        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; } = new();
        public override Stream Body { get; set; } = new MemoryStream();
        public override HttpCookies Cookies => throw new NotSupportedException();
    }
}