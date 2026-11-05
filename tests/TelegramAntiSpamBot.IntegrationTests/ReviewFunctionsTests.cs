using System.Text.Json.Nodes;

namespace TelegramAntiSpamBot.IntegrationTests;

public class ReviewFunctionsTests
{
    private const string ReportUpdate = """
        {"update_id":20,"message":{"message_id":8,"date":1700000001,
        "chat":{"id":-100,"type":"supergroup","title":"Test"},
        "from":{"id":99,"is_bot":false,"first_name":"Reporter"},"text":"/report",
        "reply_to_message":{"message_id":7,"date":1700000000,
        "chat":{"id":-100,"type":"supergroup"},
        "from":{"id":42,"is_bot":false,"first_name":"Alice"},"text":"Hi"}}}
        """;

    [Theory]
    [InlineData("/report")]
    [InlineData("/REPORT")]
    [InlineData("/report@TestBot")]
    public async Task TrustedReportRechecksShortMessageAndEstablishedSenderWithoutCache(string command)
    {
        using var fixture = new Fixture(reportCommands: true)
        {
            TargetMessageCount = 150,
            Shortcut = ShortcutCheckResult.NotSpam
        };

        var response = await fixture.Webhook.Run(fixture.Request(ReportUpdate.Replace("/report", command)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Hi", fixture.Detector.ClassifiedPayload!.UserMessage);
        Assert.True(fixture.Detector.IsReported);
        Assert.Equal(150, fixture.Detector.UserMessageCount);
        Assert.Equal(0, fixture.HashChecks);
        Assert.Equal(0, fixture.CounterIncreases);
        Assert.Empty(fixture.History);
        Assert.Equal("/report", fixture.PreviewCall!.Value.Command);
        Assert.Equal(7, fixture.SavedMessage!.MessageId);
        Assert.Equal(42, fixture.SavedMessage.UserId);
        var restriction = Assert.Single(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
        Assert.Equal(42, restriction.Body.GetProperty("user_id").GetInt64());
        Assert.Equal(1, fixture.SpamStatsUpdates);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public async Task ReportRequiresMoreThanThirtyMessages(int count)
    {
        using var fixture = new Fixture(reportCommands: true) { ReporterMessageCount = count };

        await fixture.Webhook.Run(fixture.Request(ReportUpdate));

        Assert.Null(fixture.Detector.ClassifiedPayload);
        Assert.Null(fixture.SavedMessage);
        Assert.Empty(fixture.History);
        Assert.Equal(0, fixture.CounterIncreases);
        Assert.DoesNotContain(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
    }

    [Fact]
    public async Task NonSpamReportDoesNotRewriteHistoryCountersOrHashDecisions()
    {
        using var fixture = new Fixture(reportCommands: true);
        fixture.Detector.Result = new(ResultType.Evaluated, 90);

        await fixture.Webhook.Run(fixture.Request(ReportUpdate));

        Assert.True(fixture.Detector.IsReported);
        Assert.Null(fixture.SavedMessage);
        Assert.Empty(fixture.History);
        Assert.Empty(fixture.HashDecisions);
        Assert.Equal(0, fixture.CounterIncreases);
        Assert.DoesNotContain(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
    }

    [Fact]
    public async Task ReportWithoutReplyDoesNotCheckSpam()
    {
        using var fixture = new Fixture(reportCommands: true);
        var update = JsonNode.Parse(ReportUpdate)!;
        update["message"]!.AsObject().Remove("reply_to_message");

        await fixture.Webhook.Run(fixture.Request(update.ToJsonString()));

        Assert.Null(fixture.Detector.ClassifiedPayload);
        Assert.Empty(fixture.History);
    }

    [Theory]
    [InlineData("anonymous-reporter")]
    [InlineData("anonymous-target")]
    [InlineData("other-chat")]
    public async Task InvalidReportAuthorsOrChatDoNotCheckSpam(string invalidCase)
    {
        using var fixture = new Fixture(reportCommands: true);
        var update = JsonNode.Parse(ReportUpdate)!;
        var message = update["message"]!;
        var target = message["reply_to_message"]!;
        if (invalidCase == "other-chat")
            target["chat"]!["id"] = -200;
        else
            (invalidCase == "anonymous-reporter" ? message : target)["sender_chat"] =
                JsonNode.Parse("{\"id\":-100,\"type\":\"supergroup\",\"title\":\"Test\"}");

        await fixture.Webhook.Run(fixture.Request(update.ToJsonString()));

        Assert.Null(fixture.Detector.ClassifiedPayload);
        Assert.DoesNotContain(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
        Assert.Empty(fixture.History);
    }

    [Fact]
    public async Task ReportReanalyzesReplyPhotoWithoutCaption()
    {
        using var fixture = new Fixture(reportCommands: true);
        var update = JsonNode.Parse(ReportUpdate)!;
        var target = update["message"]!["reply_to_message"]!.AsObject();
        target.Remove("text");
        target["photo"] = JsonNode.Parse("[{\"file_id\":\"photo\",\"file_unique_id\":\"unique\",\"width\":100,\"height\":100}]");

        await fixture.Webhook.Run(fixture.Request(update.ToJsonString()));

        Assert.True(fixture.Detector.IsReported);
        Assert.Equal(fixture.Detector.ImageContent, fixture.Detector.ClassifiedPayload!.ImageText);
        Assert.Null(fixture.Detector.ClassifiedPayload.UserMessage);
        Assert.Equal(7, fixture.SavedMessage!.MessageId);
        Assert.Empty(fixture.History);
        Assert.Equal(0, fixture.CounterIncreases);
    }

    [Fact]
    public async Task ReportRespectsSharedCommandCooldown()
    {
        using var fixture = new Fixture(reportCommands: true) { AllowCommandCall = false };

        await fixture.Webhook.Run(fixture.Request(ReportUpdate));

        Assert.Null(fixture.Detector.ClassifiedPayload);
        Assert.Equal("/report", fixture.PreviewCall!.Value.Command);
        Assert.Equal(TimeSpan.FromMinutes(5), fixture.PreviewCall.Value.Cooldown);
        Assert.Empty(fixture.History);
    }

    [Fact]
    public async Task ReportAiFailureDoesNotModerateTarget()
    {
        using var fixture = new Fixture(reportCommands: true);
        fixture.Detector.Result = new(ResultType.Error);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Webhook.Run(fixture.Request(ReportUpdate)));

        Assert.Null(fixture.SavedMessage);
        Assert.DoesNotContain(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
        Assert.Empty(fixture.History);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HiddenLinksReachSpamDetectionHistoryAndReview(bool caption, bool spam)
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new(ResultType.Evaluated, spam ? 99 : 10);
        var update = JsonNode.Parse(caption ? PhotoUpdate : ReportUpdate)!;
        if (!caption)
            update["message"] = update["message"]!["reply_to_message"]!.DeepClone();
        var message = update["message"]!;
        const string text = "😀 Discuss Qwen in Polzastas and Documentation for details";
        const string chatUrl = "https://t.me/+qVhOdjhm7sRhZjg6";
        const string docsUrl = "https://example.test/docs";
        message[caption ? "caption" : "text"] = text;
        message[caption ? "caption_entities" : "entities"] = new JsonArray(
            new JsonObject { ["type"] = "text_link", ["offset"] = text.IndexOf("Polzastas", StringComparison.Ordinal),
                ["length"] = "Polzastas".Length, ["url"] = chatUrl },
            new JsonObject { ["type"] = "text_link", ["offset"] = text.IndexOf("Documentation", StringComparison.Ordinal),
                ["length"] = "Documentation".Length, ["url"] = docsUrl });
        var expected = text.Replace("Polzastas", $"Polzastas ({chatUrl})")
            .Replace("Documentation", $"Documentation ({docsUrl})");

        await fixture.Webhook.Run(fixture.Request(update.ToJsonString()));

        Assert.Equal(expected, fixture.Detector.ClassifiedPayload!.UserMessage);
        if (spam)
            Assert.Equal(expected, fixture.SavedMessage!.Message);
        else
            Assert.Equal(expected, Assert.Single(fixture.History).Content.Payload.UserMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoredReplyContextRetainsHiddenLinks(bool spam)
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new(ResultType.Evaluated, spam ? 99 : 10);
        var update = JsonNode.Parse(ReportUpdate)!;
        update["message"]!["text"] = "Discuss the linked Qwen cases in this message";
        var target = update["message"]!["reply_to_message"]!;
        target["text"] = "Polzastas";
        target["entities"] = JsonNode.Parse("""
            [{"type":"text_link","offset":0,"length":9,"url":"https://t.me/example"}]
            """);

        await fixture.Webhook.Run(fixture.Request(update.ToJsonString()));

        var replyText = spam ? fixture.SavedMessage!.ReplyTo!.Text : Assert.Single(fixture.History).Content.ReplyTo!.Text;
        Assert.Equal("Polzastas (https://t.me/example)", replyText);
    }

    [Fact]
    public async Task ReportRecheckIncludesHiddenReplyLinks()
    {
        using var fixture = new Fixture(reportCommands: true);
        var update = JsonNode.Parse(ReportUpdate)!;
        var target = update["message"]!["reply_to_message"]!;
        target["text"] = "Polzastas";
        target["entities"] = JsonNode.Parse("""
            [{"type":"text_link","offset":0,"length":9,"url":"https://t.me/example"}]
            """);

        await fixture.Webhook.Run(fixture.Request(update.ToJsonString()));

        Assert.True(fixture.Detector.IsReported);
        Assert.Equal("Polzastas (https://t.me/example)", fixture.Detector.ClassifiedPayload!.UserMessage);
    }

    [Fact]
    public async Task HiddenLinksIgnoreInvalidEntitiesAndDuplicateAnnotations()
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new(ResultType.Evaluated, 10);
        var update = JsonNode.Parse(ReportUpdate)!;
        update["message"] = update["message"]!["reply_to_message"]!.DeepClone();
        update["message"]!["text"] = "Polzastas";
        update["message"]!["entities"] = JsonNode.Parse("""
            [
              {"type":"text_link","offset":0,"length":9,"url":"https://t.me/example"},
              {"type":"text_link","offset":0,"length":9,"url":"https://t.me/example"},
              {"type":"bold","offset":0,"length":9},
              {"type":"url","offset":0,"length":9},
              {"type":"text_link","offset":-1,"length":9,"url":"https://t.me/invalid"},
              {"type":"text_link","offset":0,"length":0,"url":"https://t.me/invalid"},
              {"type":"text_link","offset":0,"length":2147483647,"url":"https://t.me/invalid"},
              {"type":"text_link","offset":2147483647,"length":1,"url":"https://t.me/invalid"},
              {"type":"text_link","offset":0,"length":9}
            ]
            """);

        await fixture.Webhook.Run(fixture.Request(update.ToJsonString()));

        Assert.Equal("Polzastas (https://t.me/example)", fixture.Detector.ClassifiedPayload!.UserMessage);
        Assert.Equal(fixture.Detector.ClassifiedPayload, Assert.Single(fixture.History).Content.Payload);
    }

    [Fact]
    public async Task HiddenLinkDestinationsAffectCacheHashes()
    {
        using var first = new Fixture();
        using var second = new Fixture();
        first.Detector.Result = second.Detector.Result = new(ResultType.Evaluated, 10);
        var update = JsonNode.Parse(ReportUpdate)!;
        update["message"] = update["message"]!["reply_to_message"]!.DeepClone();
        update["message"]!["text"] = "Some interesting Qwen cases in Polzastas";
        update["message"]!["entities"] = JsonNode.Parse("""
            [{"type":"text_link","offset":31,"length":9,"url":"https://t.me/alpha"}]
            """);

        await first.Webhook.Run(first.Request(update.ToJsonString()));
        await second.Webhook.Run(second.Request(update.ToJsonString().Replace("https://t.me/alpha", "https://t.me/beta")));

        Assert.NotEqual(Assert.Single(first.HashDecisions).Hash, Assert.Single(second.HashDecisions).Hash);
    }

    [Fact]
    public async Task ChroniclePreviewDoesNotRecordCallsOrPostToTelegram()
    {
        using var fixture = new Fixture(commandCooldown: TimeSpan.FromMinutes(2));
        var response = await fixture.HistoryFunctions.PreviewChronicle(fixture.Request(), -100);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(fixture.PreviewCall);
        Assert.Equal(1, fixture.PreviewGenerations);
        Assert.Empty(fixture.Telegram.Calls);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        Assert.Equal("Preview summary", document.RootElement.GetProperty("summary").GetString());
        Assert.False(document.RootElement.GetProperty("skipped").GetBoolean());
    }

    [Fact]
    public async Task ChroniclePreviewBypassesCooldownOnRepeatedCalls()
    {
        using var fixture = new Fixture { AllowCommandCall = false };
        await fixture.HistoryFunctions.PreviewChronicle(fixture.Request(), -100);
        var response = await fixture.HistoryFunctions.PreviewChronicle(fixture.Request(), -100);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, fixture.PreviewGenerations);
        Assert.Null(fixture.PreviewCall);
        Assert.Empty(fixture.Telegram.Calls);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        Assert.False(document.RootElement.GetProperty("skipped").GetBoolean());
        Assert.Equal("Preview summary", document.RootElement.GetProperty("summary").GetString());
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
            Assert.Equal("Original text", row.GetProperty("content").GetProperty("payload").GetProperty("userMessage").GetString());
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
            Message = new ReviewMessage(-100, 7, 42, new ReviewMessageFrom("Alice", null), null,
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
        var sanitizedPayload = new MessagePayload(
            ImageText: MessageContentSanitizer.Sanitize(message.ImageText),
            UserMessage: MessageContentSanitizer.Sanitize(message.Message));
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.Unicode.GetBytes(
            JsonSerializer.Serialize(sanitizedPayload)))), message.ContentHash);
        Assert.Empty(fixture.HashDecisions);
        Assert.Empty(fixture.History);
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
        Assert.Single(fixture.History);
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
        Assert.Empty(fixture.History);
        Assert.Empty(fixture.HashDecisions);
        Assert.Contains(fixture.Telegram.Calls, call => call.Method == "deletemessage");
        Assert.Contains(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
    }

    [Fact]
    public async Task AiErrorThrowsWithoutDecisions()
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new SpamRequestResult(ResultType.Error);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Webhook.Run(fixture.Request(PhotoUpdate)));

        Assert.Equal("Spam detection service returned an error.", exception.Message);
        Assert.Null(fixture.SavedMessage);
        Assert.Empty(fixture.History);
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

    [Theory]
    [InlineData("/chronicle")]
    [InlineData("/stat")]
    public async Task WebhookRoutesCommandsWithoutSavingHistoryOrClassifying(string command)
    {
        using var fixture = new Fixture();
        var update = JsonSerializer.Serialize(new
        {
            update_id = 1,
            message = new
            {
                message_id = 7,
                date = 1700000000,
                chat = new { id = -100, type = "supergroup", title = "Test" },
                from = new { id = 42, is_bot = false, first_name = "Alice" },
                text = command
            }
        });

        var response = await fixture.Webhook.Run(fixture.Request(update));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, fixture.Commands.Calls);
        Assert.Empty(fixture.History);
        Assert.False(fixture.SpamChecked.Task.IsCompleted);
        Assert.Null(fixture.Detector.ClassifiedText);
        Assert.Single(fixture.SavedChats);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\n")]
    public async Task WebhookSkipsEmptyAndServiceMessagesInHistory(string? text)
    {
        using var fixture = new Fixture();
        var update = JsonSerializer.Serialize(new
        {
            update_id = 1,
            message = new
            {
                message_id = 7,
                date = 1700000000,
                chat = new { id = -100, type = "supergroup", title = "Test" },
                from = new { id = 42, is_bot = false, first_name = "Alice" },
                new_chat_members = new[] { new { id = 42, is_bot = false, first_name = "Alice" } },
                text
            }
        });

        var response = await fixture.Webhook.Run(fixture.Request(update));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(fixture.History);
        Assert.Single(fixture.SavedChats);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\n")]
    public async Task WebhookSavesImageContentWithoutMeaningfulCaptionInHistory(string caption)
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new SpamRequestResult(ResultType.Evaluated, 10);
        var update = PhotoUpdate.Replace("\"Original Caption 123!\"", JsonSerializer.Serialize(caption));

        var response = await fixture.Webhook.Run(fixture.Request(update));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var payload = Assert.Single(fixture.History).Content.Payload;
        Assert.Equal(fixture.Detector.ImageContent, payload.ImageText);
        Assert.Null(payload.UserMessage);
    }

    [Fact]
    public async Task WebhookSavesCaptionAndImageContentToHistory()
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new SpamRequestResult(ResultType.Evaluated, 10);

        await fixture.Webhook.Run(fixture.Request(PhotoUpdate));

        var message = Assert.Single(fixture.History);
        Assert.Equal(-100, message.ChatId);
        Assert.Equal(7, message.MessageId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime.Ticks, message.DateTimeTicks);
        Assert.Equal("Original Caption 123!", message.Content.Payload.UserMessage);
        Assert.Equal(fixture.Detector.ImageContent, message.Content.Payload.ImageText);
        Assert.Equal(fixture.Detector.ClassifiedPayload, message.Content.Payload);
        Assert.Equal(new HistoryMessageAuthor(42, "Alice Smith", "alice"), message.Content.From);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WebhookSavesImageDescriptionAndFullTextIncludingNonSpamShortcut(bool shortcut)
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new SpamRequestResult(ResultType.Evaluated, 10);
        fixture.Detector.ImageDescription = "A poster with a cat";
        fixture.Detector.ImageContent = "OCR \"quoted\"\nsecond line";
        if (shortcut)
            fixture.Shortcut = ShortcutCheckResult.NotSpam;

        await fixture.Webhook.Run(fixture.Request(PhotoUpdate));

        var content = Assert.Single(fixture.History).Content.Payload;
        Assert.Equal(new MessagePayload(fixture.Detector.ImageDescription, fixture.Detector.ImageContent,
            "Original Caption 123!"), content);
        if (shortcut)
            Assert.Null(fixture.Detector.ClassifiedPayload);
        else
            Assert.Same(content, fixture.Detector.ClassifiedPayload);
    }

    [Fact]
    public async Task CacheHashPreservesTypedFieldBoundaries()
    {
        using var first = new Fixture();
        using var second = new Fixture();
        first.Detector.Result = second.Detector.Result = new SpamRequestResult(ResultType.Evaluated, 10);
        first.Detector.ImageContent = "CD";
        second.Detector.ImageContent = "BCD";

        await first.Webhook.Run(first.Request(PhotoUpdate.Replace("Original Caption 123!", "AB")));
        await second.Webhook.Run(second.Request(PhotoUpdate.Replace("Original Caption 123!", "A")));

        Assert.NotEqual(Assert.Single(first.HashDecisions).Hash, Assert.Single(second.HashDecisions).Hash);
    }

    [Fact]
    public async Task WebhookSavesDescriptionAndTranscriptionForSpamReview()
    {
        using var fixture = new Fixture();
        fixture.Detector.ImageContent = "A poster\nFull poster text";

        await fixture.Webhook.Run(fixture.Request(PhotoUpdate));

        Assert.Equal(fixture.Detector.ImageContent, Assert.IsType<ReviewMessage>(fixture.SavedMessage).ImageText);
        Assert.Empty(fixture.History);
    }

    [Fact]
    public async Task WebhookSavesImageDescriptionWithoutCaptionOrVisibleText()
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new SpamRequestResult(ResultType.Evaluated, 10);
        fixture.Detector.ImageDescription = "A cat sleeping on a sofa";
        fixture.Detector.ImageContent = string.Empty;
        var update = PhotoUpdate.Replace("\"caption\":\"Original Caption 123!\",", "");

        await fixture.Webhook.Run(fixture.Request(update));

        var payload = Assert.Single(fixture.History).Content.Payload;
        Assert.Equal(fixture.Detector.ImageDescription, payload.ImageDescription);
        Assert.Null(payload.ImageText);
        Assert.Null(payload.UserMessage);
        Assert.Same(payload, fixture.Detector.ClassifiedPayload);
    }

    [Fact]
    public async Task WebhookSkipsPhotoWithoutCaptionOrAnalysisContent()
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new SpamRequestResult(ResultType.Error);
        fixture.Detector.ImageContent = string.Empty;
        var update = PhotoUpdate.Replace("\"caption\":\"Original Caption 123!\",", "");

        var response = await fixture.Webhook.Run(fixture.Request(update));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(fixture.Detector.ClassifiedPayload);
        Assert.Empty(fixture.HashDecisions);
        Assert.False(fixture.Deleted);
        Assert.Empty(fixture.History);
    }

    [Theory]
    [InlineData("Original Caption 123!")]
    [InlineData("Hi")]
    public async Task WebhookClassifiesAndSavesCaptionWhenImageAnalysisReturnsNoContent(string caption)
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new SpamRequestResult(ResultType.Evaluated, 10);
        fixture.Detector.ImageContent = string.Empty;
        var update = PhotoUpdate.Replace("Original Caption 123!", caption);

        var response = await fixture.Webhook.Run(fixture.Request(update));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new MessagePayload(UserMessage: caption), fixture.Detector.ClassifiedPayload);
        Assert.Equal(fixture.Detector.ClassifiedPayload, Assert.Single(fixture.History).Content.Payload);
        Assert.False(fixture.Deleted);
    }

    [Fact]
    public async Task WebhookSavesChannelPhotoContentWithoutUserModeration()
    {
        using var fixture = new Fixture();
        fixture.Detector.ImageContent = "A poster\nFull poster text";
        var update = PhotoUpdate.Replace("\"message\":", "\"channel_post\":")
            .Replace("\"from\":{\"id\":42,\"is_bot\":false,\"first_name\":\"Alice\",\"last_name\":\"Smith\",\"username\":\"alice\"},", "");

        await fixture.Webhook.Run(fixture.Request(update));

        var history = Assert.Single(fixture.History);
        Assert.Equal(fixture.Detector.ImageContent, history.Content.Payload.ImageText);
        Assert.Equal(HistoryUpdateType.ChannelPost, history.UpdateType);
        Assert.Null(fixture.Detector.ClassifiedText);
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

    [Theory]
    [InlineData("message", "edited_message", HistoryUpdateType.Message, HistoryUpdateType.EditedMessage)]
    [InlineData("channel_post", "edited_channel_post", HistoryUpdateType.ChannelPost, HistoryUpdateType.EditedChannelPost)]
    public async Task WebhookPreservesSendTimeAndTypedPayloadForEdits(string originalKey,
        string editedKey, HistoryUpdateType originalType, HistoryUpdateType editedType)
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new SpamRequestResult(ResultType.Evaluated, 10);
        string Update(string key, string text, long? editedAt) => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["update_id"] = editedAt is null ? 1 : 2,
            [key] = new
            {
                message_id = 7,
                date = 1700000000,
                edit_date = editedAt,
                from = new { id = 42, is_bot = false, first_name = "Alice" },
                chat = new { id = -100, type = "supergroup", title = "Test" },
                text
            }
        });
        await fixture.Webhook.Run(fixture.Request(Update(originalKey, "Original message", null)));
        await fixture.Webhook.Run(fixture.Request(Update(editedKey, "Edited message", 1700000060)));

        Assert.Equal(2, fixture.History.Count);
        var original = fixture.History[0];
        var edited = fixture.History[1];
        Assert.Equal(original.MessageId, edited.MessageId);
        Assert.Equal(originalType, original.UpdateType);
        Assert.Equal(editedType, edited.UpdateType);
        Assert.Equal("Original message", original.Content.Payload.UserMessage);
        Assert.Equal("Edited message", edited.Content.Payload.UserMessage);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcTicks, original.DateTimeTicks);
        Assert.Equal(original.DateTimeTicks, edited.DateTimeTicks);
        Assert.Equal(2, edited.UpdateId);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(edited));
        Assert.Equal(editedType.ToString(), serialized.RootElement.GetProperty("UpdateType").GetString());
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

    [Fact]
    public async Task WebhookSavesHistoryAfterClassificationAndAwaitsWritesBeforeReturning()
    {
        using var fixture = new Fixture();
        var classificationGate = new TaskCompletionSource<SpamRequestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var historyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Detector.ClassificationGate = classificationGate;
        fixture.HistorySaveTask = historyGate.Task;
        fixture.ChatSaveTask = chatGate.Task;

        var run = fixture.Webhook.Run(fixture.Request(PhotoUpdate));
        try
        {
            await fixture.Detector.ClassificationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(fixture.History);
            Assert.Single(fixture.SavedChats);
            Assert.False(run.IsCompleted);

            classificationGate.SetResult(new SpamRequestResult(ResultType.Evaluated, 10));
            await fixture.HistorySaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(fixture.History);
            Assert.False(run.IsCompleted);
            historyGate.SetResult();
            Assert.False(run.IsCompleted);
        }
        finally
        {
            classificationGate.TrySetResult(new SpamRequestResult(ResultType.Evaluated, 10));
            historyGate.TrySetResult();
            chatGate.TrySetResult();
        }

        Assert.Equal(HttpStatusCode.NoContent, (await run).StatusCode);
    }

    [Fact]
    public async Task HistoryFailureRethrowsAfterProcessingMessage()
    {
        using var fixture = new Fixture();
        fixture.Detector.Result = new SpamRequestResult(ResultType.Evaluated, 10);
        fixture.HistorySaveTask = Task.FromException(new InvalidOperationException("Storage unavailable"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Webhook.Run(fixture.Request(PhotoUpdate)));

        Assert.Equal("Storage unavailable", exception.Message);
        Assert.NotNull(fixture.Detector.ClassifiedText);
        Assert.DoesNotContain(fixture.Telegram.Calls, call => call.Method == "deletemessage");
        Assert.Single(fixture.SavedChats);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WebhookMissingSpamMessageStillRestrictsUserAndUpdatesStats(bool shortcut)
    {
        using var fixture = new Fixture();
        if (shortcut)
            fixture.Shortcut = ShortcutCheckResult.Spam;
        fixture.Telegram.DeleteErrorCode = 400;
        fixture.Telegram.DeleteErrorDescription = "Bad Request: message to delete not found";

        var response = await fixture.Webhook.Run(fixture.Request(PhotoUpdate));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(fixture.Telegram.Calls, call => call.Method == "deletemessage");
        var restriction = Assert.Single(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
        Assert.Equal(-100, restriction.Body.GetProperty("chat_id").GetInt64());
        Assert.Equal(42, restriction.Body.GetProperty("user_id").GetInt64());
        Assert.Equal(1, fixture.SpamStatsUpdates);
        Assert.Empty(fixture.History);
        Assert.Equal(!shortcut, fixture.SavedMessage is not null);
    }

    [Theory]
    [InlineData(true, 400, "Bad Request: message can't be deleted")]
    [InlineData(false, 400, "Bad Request: message can't be deleted")]
    [InlineData(true, 403, "Forbidden: bot was kicked from the supergroup chat")]
    [InlineData(false, 403, "Forbidden: bot was kicked from the supergroup chat")]
    [InlineData(true, 404, "Bad Request: message to delete not found")]
    [InlineData(false, 404, "Bad Request: message to delete not found")]
    public async Task WebhookOtherSpamDeletionErrorsPropagate(bool shortcut, int errorCode, string description)
    {
        using var fixture = new Fixture();
        if (shortcut)
            fixture.Shortcut = ShortcutCheckResult.Spam;
        fixture.Telegram.DeleteErrorCode = errorCode;
        fixture.Telegram.DeleteErrorDescription = description;

        var exception = await Assert.ThrowsAsync<Telegram.Bot.Exceptions.ApiRequestException>(() =>
            fixture.Webhook.Run(fixture.Request(PhotoUpdate)));

        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.Equal(description, exception.Message);
        Assert.DoesNotContain(fixture.Telegram.Calls, call => call.Method == "restrictchatmember");
        Assert.Equal(0, fixture.SpamStatsUpdates);
        Assert.Empty(fixture.History);
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
        public TaskCompletionSource HistorySaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SpamChecked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public (long? ChatId, int PageSize, string? Token)? Query { get; private set; }
        public ReviewFunctions Functions { get; }
        public AntiSpamBotWebHook Webhook { get; }
        public DetectorStub Detector { get; } = new();
        public CommandStub Commands { get; } = new();
        public ShortcutCheckResult Shortcut { get; set; } = ShortcutCheckResult.NotFound;
        public ReviewMessage? SavedMessage { get; private set; }
        public List<ReviewChat> SavedChats { get; } = [];
        public bool ChatSaveFails { get; set; }
        public (int PageSize, string? Token)? ChatQuery { get; private set; }
        public int SpamStatsUpdates { get; private set; }
        public int ReporterMessageCount { get; set; } = 31;
        public int TargetMessageCount { get; set; }
        public int CounterIncreases { get; private set; }
        public int HashChecks { get; private set; }

        public Fixture(TimeSpan? spamRestrictionTimeout = null, TimeSpan? commandCooldown = null, bool reportCommands = false)
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
                        Assert.IsType<CancellationToken>(arguments[1]);
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
                        HistorySaveStarted.TrySetResult();
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
                        return Task.FromResult((long)arguments[1]! == 99 ? ReporterMessageCount : TargetMessageCount);
                    case nameof(IMessageCountRepository.IncreaseMessageCount):
                        CounterIncreases++;
                        return Task.CompletedTask;
                    case nameof(ISpamStatsRepository.UpdateChannelSpamStats):
                        SpamStatsUpdates++;
                        return Task.CompletedTask;
                    case nameof(ISpamHashRepository.IsSpam):
                        HashChecks++;
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
                CreateRepository<IChronicleService>(), NullLogger<HistoryReviewFunctions>.Instance);
            var processor = new SpamMessageProcessor(NullLogger<SpamMessageProcessor>.Instance,
                Detector, HistoryRepository, probablySpamRepository, messageCountRepository, spamHashRepository,
                spamStatsRepository, bot, new TelegramImageUrlResolver(token), botConfig);
            ICommandProcessor commands = reportCommands
                ? new CommandProcessor([new TelegramAntiSpamBot.Commands.Commands.ReportCommand(messageCountRepository,
                    processor, NullLogger<TelegramAntiSpamBot.Commands.Commands.ReportCommand>.Instance)],
                    botConfig.Value, bot, CreateRepository<ICommandCallsRepository>(), NullLogger<CommandProcessor>.Instance)
                : Commands;
            Webhook = new AntiSpamBotWebHook(NullLogger<AntiSpamBotWebHook>.Instance,
                chatsRepository, processor, commands, botConfig);
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
        public int? DeleteErrorCode { get; set; }
        public string? DeleteErrorDescription { get; set; }
        private int nextMessageId = 99;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var method = request.RequestUri!.Segments[^1].ToLowerInvariant();
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Calls.Add((method, document.RootElement.Clone()));
            if (method == "deletemessage" && DeleteErrorCode is { } errorCode)
                return new HttpResponseMessage((HttpStatusCode)errorCode)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        ok = false,
                        error_code = errorCode,
                        description = DeleteErrorDescription
                    }), Encoding.UTF8, "application/json")
                };
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
        public MessagePayload? ClassifiedPayload { get; private set; }
        public string? ClassifiedText => ClassifiedPayload is null ? null : string.Join("\n",
            new[] { ClassifiedPayload.UserMessage, ClassifiedPayload.ImageDescription, ClassifiedPayload.ImageText }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        public TaskCompletionSource<SpamRequestResult>? ClassificationGate { get; set; }
        public TaskCompletionSource ClassificationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ImageContent { get; set; } = "OCR \"quoted\"\nsecond line";
        public string ImageDescription { get; set; } = string.Empty;
        public Task<ImageAnalysisResult> ExtractImageText(string imageUrl) =>
            Task.FromResult(new ImageAnalysisResult { TextOnImage = ImageContent, ImageDescription = ImageDescription });
        public bool IsReported { get; private set; }
        public int UserMessageCount { get; private set; }
        public Task<SpamRequestResult> IsSpamAsync(MessagePayload userMessage, int userMessagesCount, bool explainDecision = false,
            bool isReported = false)
        {
            IsReported = isReported;
            UserMessageCount = userMessagesCount;
            ClassifiedPayload = userMessage;
            ClassificationStarted.TrySetResult();
            return ClassificationGate?.Task ?? Task.FromResult(Result);
        }
    }

    private sealed class CommandStub : ICommandProcessor
    {
        public int Calls { get; private set; }
        public bool IsCommand(string? message) => message is "/chronicle" or "/stat";
        public Task ProcessCommand(Telegram.Bot.Types.Update update)
        {
            Calls++;
            return Task.CompletedTask;
        }
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