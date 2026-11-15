using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace TelegramAntiSpamBot.IntegrationTests;

public class MentionCommandTests
{
    [Theory]
    [InlineData("Answer")]
    [InlineData("!")]
    public async Task SearchSendsExactlyOneAnswerWithInlineSources(string answer)
    {
        using var fixture = new Fixture();
        fixture.Ai.Search = true;
        fixture.Ai.Answer = answer == "!" ? new string('!', 5000) : answer;

        await fixture.Processor.ProcessCommand(fixture.Update);

        var texts = fixture.Telegram.Sent.Select(sent => sent.GetProperty("text").GetString()!).ToArray();
        Assert.Single(texts);
        Assert.All(texts, part => Assert.InRange(part.Length, 1, 4096));
        Assert.Equal(TelegramMarkdownFormatter.Format(fixture.Ai.Answer.Insert(6, " [Official source](https://example.com/source)")), texts[0]);
        Assert.Contains("[Official source](https://example.com/source)", texts[0]);
        Assert.Equal(1000, fixture.Telegram.Sent[0].GetProperty("reply_parameters").GetProperty("message_id").GetInt32());
        Assert.Equal(1, fixture.Ai.Requests);
    }

    [Fact]
    public async Task UnexpectedSourceOverflowStillSendsOneBoundedMessageWithoutSourceLengthError()
    {
        using var fixture = new Fixture();
        fixture.Ai.Search = true;
        fixture.Ai.SourceUrls = Enumerable.Range(0, 100)
            .Select(index => $"https://example.com/source/{index}?q={new string('a', 50)}").ToArray();

        var answer = await fixture.Service.GenerateAsync(new("Question", null, null));
        Assert.Equal("Answer " + string.Join(" ", fixture.Ai.SourceUrls.Select(url => $"[Official source]({url})")), answer.Answer);
        Assert.Equal(fixture.Ai.SourceUrls, answer.Sources);

        await fixture.Processor.ProcessCommand(fixture.Update);
        Assert.Single(fixture.Telegram.Sent);
        var texts = fixture.Telegram.Sent.Select(sent => sent.GetProperty("text").GetString()!).ToArray();
        Assert.All(texts, part => Assert.InRange(part.Length, 1, 4096));
        Assert.Contains($"[Official source]({fixture.Ai.SourceUrls[0]})", texts[0]);
        Assert.EndsWith(")", texts[0].TrimEnd());
    }

    [Fact]
    public async Task ThreeSourcesAreInlineInOneMessageAndPromptRequestsSynthesis()
    {
        using var fixture = new Fixture();
        fixture.Ai.Search = true;
        fixture.Ai.SourceUrls = ["https://example.com/one", "https://example.com/two", "https://example.com/three"];

        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Single(fixture.Telegram.Sent);
        Assert.Equal("Answer [Official source](https://example.com/one) [Official source](https://example.com/two) [Official source](https://example.com/three)",
            fixture.Telegram.Sent[0].GetProperty("text").GetString());
        Assert.Contains("at most three sources", fixture.Ai.Prompt);
        Assert.Contains("Synthesize their relevant evidence", fixture.Ai.Prompt);
    }

    [Fact]
    public async Task ProviderCitationMarkersBecomeTitledLinksInTextOrderAndPreserveSourceUrls()
    {
        using var fixture = new Fixture();
        fixture.Ai.Search = true;
        var first = "\uE200cite\uE202turn0search0\uE201";
        var second = "\uE200cite\uE202turn0search1\uE201";
        var third = "\uE200cite\uE202turn0search2\uE201";
        fixture.Ai.Answer = $"First. {first} Second. {second} Again. {third}";
        object Citation(string marker, string url)
        {
            var start = fixture.Ai.Answer.IndexOf(marker, StringComparison.Ordinal);
            return new { type = "url_citation", start_index = start, end_index = start + marker.Length, title = "Source", url };
        }
        fixture.Ai.Annotations = [Citation(third, "https://example.com/one"),
            Citation(first, "https://example.com/one"), Citation(second, "https://example.com/two")];

        var response = await fixture.Service.GenerateAsync(new("Question", null, null));

        Assert.Equal("First. [Source](https://example.com/one) Second. [Source](https://example.com/two) Again. [Source](https://example.com/one)", response.Answer);
        Assert.Equal(new[] { "https://example.com/one", "https://example.com/two" }, response.Sources);
    }

    [Theory]
    [InlineData("\ud83d\ude00 Fact.", "\ud83d\ude00 Fact. [Source](https://example.com/source)")]
    [InlineData("Fact. [Source](https://example.com/source)", "Fact. [Source](https://example.com/source)")]
    public async Task CitationPreservesProseAndUnicodeOrReplacesSourceLink(string text, string expected)
    {
        using var fixture = new Fixture();
        fixture.Ai.Search = true;
        fixture.Ai.Answer = text;
        var start = text.IndexOf("[Source]", StringComparison.Ordinal);
        fixture.Ai.Annotations = [new { type = "url_citation", start_index = Math.Max(start, 0),
            end_index = text.Length, title = "Source", url = "https://example.com/source" },
            new { type = "url_citation", start_index = Math.Max(start, 0), end_index = text.Length,
                title = "Duplicate", url = "https://example.com/source" }];

        var response = await fixture.Service.GenerateAsync(new("Question", null, null));

        Assert.Equal(expected, response.Answer);
        Assert.Equal("https://example.com/source", Assert.Single(response.Sources));
    }

    [Theory]
    [InlineData("Source [details] *title*", "[Source \\[details\\] *title*](https://example.com/source)")]
    [InlineData(null, "[https://example.com/source](https://example.com/source)")]
    public async Task CitationPreservesAndEscapesProvidedTitleOrUsesUrlWhenTitleIsAbsent(string? title, string link)
    {
        using var fixture = new Fixture();
        fixture.Ai.Search = true;
        fixture.Ai.Annotations = [new { type = "url_citation", start_index = 0, end_index = 6, title,
            url = "https://example.com/source" }];

        var response = await fixture.Service.GenerateAsync(new("Question", null, null));

        Assert.Equal("Answer " + link, response.Answer);
    }

    [Fact]
    public async Task InvalidCitationRangeIsReportedInsteadOfCorruptingAnswer()
    {
        using var fixture = new Fixture();
        fixture.Ai.Search = true;
        fixture.Ai.Annotations = [new { type = "url_citation", start_index = 0, end_index = 100,
            title = "Source", url = "https://example.com/source" }];
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GenerateAsync(new("Question", null, null)));
    }

    [Fact]
    public async Task IncompleteAgentAnswerIsRejected()
    {
        using var fixture = new Fixture();
        fixture.Ai.Status = "incomplete";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GenerateAsync(new("Question", null, null)));
    }

    [Fact]
    public async Task CancellationIsPropagatedToAgent()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.GenerateAsync(new("Question", null, null), cancellation.Token));
        Assert.Equal(0, fixture.Ai.Requests);
    }

    [Fact]
    public async Task AgentCallsDoNotShareConversationHistory()
    {
        using var fixture = new Fixture();
        await fixture.Service.GenerateAsync(new("First", null, null));
        await fixture.Service.GenerateAsync(new("Second", null, null));
        Assert.Equal("Second", fixture.Ai.Input!.Question);
        Assert.Equal(2, fixture.Ai.Requests);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task MentionRetriesRateLimitAndReturnsAnswer(int failures)
    {
        using var fixture = new Fixture();
        fixture.Ai.RateLimitResponses = failures;

        var answer = await fixture.Service.GenerateAsync(new MentionRequest("Question", null, null));

        Assert.Equal("Answer", answer.Answer);
        Assert.Equal(failures + 1, fixture.Ai.Requests);
    }

    [Fact]
    public async Task MentionStopsAfterFiveRetries()
    {
        using var fixture = new Fixture();
        fixture.Ai.RateLimitResponses = 10;

        var exception = await Assert.ThrowsAsync<System.ClientModel.ClientResultException>(() =>
            fixture.Service.GenerateAsync(new MentionRequest("Question", null, null)));

        Assert.Equal(429, exception.Status);
        Assert.Equal(6, fixture.Ai.Requests);
    }

    [Fact]
    public async Task MentionDoesNotRetryBadRequest()
    {
        using var fixture = new Fixture();
        fixture.Ai.Fail = true;

        var exception = await Assert.ThrowsAsync<System.ClientModel.ClientResultException>(() =>
            fixture.Service.GenerateAsync(new MentionRequest("Question", null, null)));

        Assert.Equal(400, exception.Status);
        Assert.Equal(1, fixture.Ai.Requests);
    }

    [Theory]
    [InlineData("@TestBot")]
    [InlineData("@testbot какая погода?")]
    [InlineData("@TESTBOT\nWhat is this?")]
    [InlineData("@TestBot\tQuestion")]
    public async Task LeadingMentionIsACommandAndIsNotDeleted(string text)
    {
        using var fixture = new Fixture();
        fixture.Update.Message!.Text = text;
        Assert.True(fixture.Processor.IsCommand(text));

        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Empty(fixture.Telegram.Deleted);
        Assert.Equal(1, fixture.Calls.Count);
        Assert.Equal("@TestBot", fixture.Calls.Command);
        Assert.Equal(TimeSpan.FromMinutes(5), fixture.Calls.Cooldown);
        Assert.Equal(1, fixture.Ai.Requests);
        Assert.Equal(text[8..].Trim(), fixture.Ai.Input!.Question);
        var sent = Assert.Single(fixture.Telegram.Sent);
        Assert.Equal("MarkdownV2", sent.GetProperty("parse_mode").GetString());
        Assert.Equal(1000, sent.GetProperty("reply_parameters").GetProperty("message_id").GetInt32());
        Assert.Equal("Answer", sent.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ordinary message")]
    [InlineData("hello @TestBot question")]
    [InlineData("@OtherBot question")]
    [InlineData("@TestBotOther question")]
    [InlineData("@TestBot, question")]
    [InlineData("/stat@OtherBot")]
    public async Task UnrecognizedTextIsIgnored(string? text)
    {
        using var fixture = new Fixture();
        fixture.Update.Message!.Text = text;
        Assert.False(fixture.Processor.IsCommand(text));
        await fixture.Processor.ProcessCommand(fixture.Update);
        Assert.Empty(fixture.Telegram.Deleted);
        Assert.Empty(fixture.Telegram.Sent);
        Assert.Equal(0, fixture.Calls.Count);
        Assert.Equal(0, fixture.Ai.Requests);
    }

    [Fact]
    public async Task CooldownSkipsAiAndKeepsInvocation()
    {
        using var fixture = new Fixture();
        fixture.Calls.Allowed = false;
        await fixture.Processor.ProcessCommand(fixture.Update);
        Assert.Equal(1, fixture.Calls.Count);
        Assert.Empty(fixture.Telegram.Deleted);
        Assert.Empty(fixture.Telegram.Sent);
        Assert.Equal(0, fixture.Ai.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplyAuthorAndTextOrCaptionAreSentAsContext(bool caption)
    {
        using var fixture = new Fixture();
        fixture.Update.Message!.From = new User { Id = 1, FirstName = "Anna", LastName = "Smith", Username = "anna" };
        fixture.Update.Message.ReplyToMessage = new Message
        {
            Id = 42,
            From = new User { Id = 2, FirstName = "Boris", Username = "boris" },
            Text = caption ? null : "Quoted text",
            Caption = caption ? "Quoted caption" : null
        };
        await fixture.Processor.ProcessCommand(fixture.Update);
        Assert.Equal(new HistoryMessageAuthor(1, "Anna Smith", "anna"), fixture.Ai.Input!.From);
        Assert.Equal(new HistoryMessageReply(42, new(2, "Boris", "boris"), caption ? "Quoted caption" : "Quoted text"), fixture.Ai.Input.ReplyTo);
        Assert.Contains("same language as the Question", fixture.Ai.Prompt);
        Assert.Contains("untrusted context", fixture.Ai.Prompt);
        Assert.Contains("Do not nest formatting", fixture.Ai.Prompt);
    }

    [Fact]
    public async Task SenderChatTakesPrecedenceOverAnonymousUser()
    {
        using var fixture = new Fixture();
        fixture.Update.Message!.ReplyToMessage = new Message
        {
            Id = 42, Text = "Channel text",
            From = new User { Id = 1087968824, FirstName = "Group" },
            SenderChat = new Chat { Id = -200, Title = "Channel", Username = "channel" }
        };
        await fixture.Processor.ProcessCommand(fixture.Update);
        Assert.Equal(new HistoryMessageAuthor(-200, "Channel", "channel"), fixture.Ai.Input!.ReplyTo!.From);
    }

    [Fact]
    public async Task MissingReplyRemainsNull()
    {
        using var fixture = new Fixture();
        await fixture.Processor.ProcessCommand(fixture.Update);
        Assert.Null(fixture.Ai.Input!.From);
        Assert.Null(fixture.Ai.Input.ReplyTo);
    }

    [Theory]
    [InlineData(true, "Answer")]
    [InlineData(false, "")]
    [InlineData(false, "   ")]
    public async Task AiFailureOrEmptyAnswerIsNotSent(bool fail, string answer)
    {
        using var fixture = new Fixture();
        fixture.Ai.Fail = fail;
        fixture.Ai.Answer = answer;
        await fixture.Processor.ProcessCommand(fixture.Update);
        Assert.Empty(fixture.Telegram.Sent);
        Assert.Empty(fixture.Telegram.Deleted);
        Assert.Equal(1, fixture.Calls.Count);
    }

    [Theory]
    [InlineData("**Bold!** _italic_ `a_b`", "*Bold\\!* _italic_ `a_b`")]
    [InlineData("*italic* and **bold**", "_italic_ and *bold*")]
    [InlineData("a.b (x) [y] #tag + - = | {z} ~ !", "a\\.b \\(x\\) \\[y\\] \\#tag \\+ \\- \\= \\| \\{z\\} \\~ \\!")]
    [InlineData("```cs\nvar x = 1;\n```", "```cs\nvar x = 1;\n```")]
    [InlineData("```\nx_y\n```", "```\nx_y\n```")]
    [InlineData("Unclosed **bold!", "Unclosed \\*\\*bold\\!")]
    public async Task AnswerIsConvertedToSafeTelegramMarkdown(string answer, string expected)
    {
        using var fixture = new Fixture();
        fixture.Ai.Answer = answer;
        await fixture.Processor.ProcessCommand(fixture.Update);
        Assert.Equal(expected, Assert.Single(fixture.Telegram.Sent).GetProperty("text").GetString());
    }

    [Fact]
    public async Task EscapingLimitKeepsFormattedSpanClosed()
    {
        using var fixture = new Fixture();
        fixture.Ai.Answer = "**" + new string('!', 3000) + "**";
        await fixture.Processor.ProcessCommand(fixture.Update);
        var texts = fixture.Telegram.Sent.Select(sent => sent.GetProperty("text").GetString()!).ToArray();
        Assert.Single(texts);
        Assert.All(texts, text =>
        {
            Assert.InRange(text.Length, 1, 4096);
            Assert.StartsWith("*", text);
            Assert.EndsWith("\\!*", text);
        });
        Assert.Equal("*" + string.Concat(Enumerable.Repeat("\\!", 2047)) + "*", texts[0]);
    }

    [Theory]
    [InlineData(3996)]
    [InlineData(3997)]
    public async Task LongAgentAnswerIsNotTruncatedOrDamaged(int prefixLength)
    {
        using var fixture = new Fixture();
        fixture.Ai.Answer = new string('a', prefixLength) + char.ConvertFromUtf32(0x1F600) + new string('b', 200);
        var answer = await fixture.Service.GenerateAsync(new("Question", null, null));
        Assert.Equal(fixture.Ai.Answer, answer.Answer);
        Assert.DoesNotContain('\uFFFD', Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(answer.Answer)));
    }

    [Fact]
    public async Task AgentPreservesAnswerAtCharacterLimit()
    {
        using var fixture = new Fixture();
        fixture.Ai.Answer = new string('a', 4000);
        Assert.Equal(fixture.Ai.Answer, (await fixture.Service.GenerateAsync(new("Question", null, null))).Answer);
    }

    [Fact]
    public async Task WithoutHistoryToolCallSendsOnlyMetadataAndDoesNotReadStorage()
    {
        using var fixture = new Fixture();
        fixture.History.Messages.AddRange(Enumerable.Range(980, 23).Select(History));
        fixture.History.Messages.Add(History(999) with { ChatId = -200 });
        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Equal(new MentionConversation(-100, 1000, new DateTimeOffset(fixture.Update.Message!.Date), null),
            fixture.Ai.Input!.Conversation);
        Assert.Equal(0, fixture.History.PageCalls);
    }

    [Fact]
    public async Task ReplySendsTimestampAndExactReplyWithoutReadingStorage()
    {
        using var fixture = new Fixture();
        var date = new DateTime(2026, 1, 1, 0, 16, 20, DateTimeKind.Utc);
        fixture.Update.Message!.ReplyToMessage = new Message { Id = 980, Text = "Target", Date = date };
        fixture.History.Messages.AddRange(Enumerable.Range(965, 40).Select(History));
        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Equal(new DateTimeOffset(date), fixture.Ai.Input!.Conversation!.ReplyDate);
        Assert.Equal(0, fixture.History.PageCalls);
        Assert.Equal("Target", fixture.Ai.Input.ReplyTo!.Text);
        Assert.Contains("Focus specifically on Question and its explicit ReplyTo", fixture.Ai.Prompt);
        Assert.Contains("Nearby messages may be entirely unrelated", fixture.Ai.Prompt);
    }

    [Fact]
    public async Task AgentRetrievesRequestedHistoryAndContinuationOnlyThroughTool()
    {
        using var fixture = new Fixture();
        fixture.Ai.HistoryCalls = 2;
        fixture.History.Messages.AddRange(Enumerable.Range(980, 10).Select(History));
        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Equal(2, fixture.History.PageCalls);
        Assert.Equal(new[] { 980, 981, 982, 983, 984, 985, 986 },
            fixture.Ai.HistoryResults[0].GetProperty("Messages").EnumerateArray().Select(item => item.GetProperty("MessageId").GetInt32()));
        Assert.Equal(new[] { 987, 988, 989 },
            fixture.Ai.HistoryResults[1].GetProperty("Messages").EnumerateArray().Select(item => item.GetProperty("MessageId").GetInt32()));
        Assert.Equal("7", fixture.Ai.HistoryResults[0].GetProperty("ContinuationToken").GetString());
        Assert.Equal(JsonValueKind.Null, fixture.Ai.HistoryResults[1].GetProperty("ContinuationToken").ValueKind);
        Assert.Equal(3, fixture.Ai.Requests);
        Assert.Single(fixture.Telegram.Sent);
    }

    [Fact]
    public async Task HistoryToolPreservesPayloadAndMetadata()
    {
        using var fixture = new Fixture();
        fixture.Ai.HistoryCalls = 1;
        var author = new HistoryMessageAuthor(2, "Boris", "boris");
        var reply = new HistoryMessageReply(40, author, "Nested reply");
        fixture.History.Messages.Add(History(998) with
        {
            UpdateId = 20, UpdateType = HistoryUpdateType.EditedMessage,
            Content = new(author, reply, "Updated caption")
        });
        fixture.History.Messages.Add(History(999) with
        {
            Content = new(null, null, new MessagePayload(ImageDescription: "Image only", ImageText: "Extracted text"))
        });
        await fixture.Processor.ProcessCommand(fixture.Update);

        var context = fixture.Ai.HistoryResults[0].GetProperty("Messages");
        Assert.Equal(2, context.GetArrayLength());
        Assert.Equal(fixture.History.Messages[0].Content,
            context[0].GetProperty("Content").Deserialize<HistoryMessageContent>());
        Assert.Equal(new DateTimeOffset(new DateTime(fixture.History.Messages[0].DateTimeTicks, DateTimeKind.Utc)),
            context[0].GetProperty("Date").GetDateTimeOffset());
        Assert.Equal("EditedMessage", context[0].GetProperty("UpdateType").GetString());
        Assert.Equal(20, context[0].GetProperty("UpdateId").GetInt32());
        Assert.Equal(fixture.History.Messages[1].Content,
            context[1].GetProperty("Content").Deserialize<HistoryMessageContent>());
    }

    [Fact]
    public async Task EmptyServerPageWithContinuationCanBeContinuedByAgent()
    {
        using var fixture = new Fixture();
        fixture.Ai.HistoryCalls = 2;
        fixture.History.EmptyFirstPage = true;
        fixture.History.Messages.Add(History(999));

        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Equal(2, fixture.History.PageCalls);
        Assert.Empty(fixture.Ai.HistoryResults[0].GetProperty("Messages").EnumerateArray());
        Assert.Equal("0", fixture.Ai.HistoryResults[0].GetProperty("ContinuationToken").GetString());
        Assert.Equal(999, Assert.Single(fixture.Ai.HistoryResults[1].GetProperty("Messages").EnumerateArray())
            .GetProperty("MessageId").GetInt32());
        Assert.Equal(JsonValueKind.Null, fixture.Ai.HistoryResults[1].GetProperty("ContinuationToken").ValueKind);
    }

    [Fact]
    public async Task HistoryToolPreservesFullTextAndNestedReply()
    {
        using var fixture = new Fixture();
        fixture.Ai.HistoryCalls = 1;
        var text = new string('a', 3996) + "😀" + new string('b', 100);
        var replyText = new string('a', 1996) + "😀" + new string('b', 100);
        fixture.History.Messages.Add(History(999) with
        {
            Content = new(null, new HistoryMessageReply(42, null, replyText), text)
        });
        await fixture.Processor.ProcessCommand(fixture.Update);

        var context = Assert.Single(fixture.Ai.HistoryResults[0].GetProperty("Messages").EnumerateArray())
            .GetProperty("Content").Deserialize<HistoryMessageContent>()!;
        Assert.Equal(text, context.Payload.UserMessage);
        Assert.Equal(replyText, context.ReplyTo!.Text);
    }

    [Fact]
    public async Task UnusedHistoryFailureDoesNotAffectAnswer()
    {
        using var fixture = new Fixture();
        fixture.History.Fail = true;
        fixture.Update.Message!.ReplyToMessage = new Message { Id = 42, Text = "Target" };
        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Equal(0, fixture.History.PageCalls);
        Assert.Equal("Question", fixture.Ai.Input!.Question);
        Assert.Equal("Target", fixture.Ai.Input.ReplyTo!.Text);
        Assert.Single(fixture.Telegram.Sent);
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00+00:00", "2026-01-01T00:00:00+00:00")]
    [InlineData("2026-01-01T01:00:00+00:00", "2026-01-01T00:00:00+00:00")]
    [InlineData("2026-01-01T00:00:00+00:00", "2026-01-01T01:00:01+00:00")]
    public async Task InvalidHistoryBoundsReturnToolErrorWithoutReadingStorage(string from, string to)
    {
        using var fixture = new Fixture();
        fixture.Ai.HistoryCalls = 1;
        fixture.Ai.HistoryFrom = from;
        fixture.Ai.HistoryTo = to;
        fixture.Ai.ExpectHistoryError = true;

        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Equal(0, fixture.History.PageCalls);
        Assert.NotEmpty(Assert.Single(fixture.Ai.HistoryErrors));
        Assert.Empty(fixture.Ai.HistoryResults);
    }

    [Fact]
    public async Task HistoryStorageFailureReachesAgentAsToolErrorRatherThanEmptyPage()
    {
        using var fixture = new Fixture();
        fixture.Ai.HistoryCalls = 1;
        fixture.Ai.ExpectHistoryError = true;
        fixture.History.Fail = true;

        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Equal(1, fixture.History.PageCalls);
        Assert.NotEmpty(Assert.Single(fixture.Ai.HistoryErrors));
        Assert.Empty(fixture.Ai.HistoryResults);
    }

    [Fact]
    public async Task CancellationDuringHistoryRetrievalStopsAgent()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Ai.HistoryCalls = 1;
        fixture.History.OnQuery = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.GenerateAsync(
            new("Question", null, null, new(-100, 1000, new DateTimeOffset(fixture.Update.Message!.Date), null)),
            cancellation.Token));

        Assert.Equal(1, fixture.History.PageCalls);
        Assert.Equal(1, fixture.Ai.Requests);
    }

    private static HistoryMessage History(int id) => new(-100, id,
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(id).Ticks,
        new(null, null, "Message " + id));

    private sealed class HistoryStub : IMessageHistoryRepository
    {
        public List<HistoryMessage> Messages { get; } = [];
        public int PageCalls { get; private set; }
        public bool Fail { get; set; }
        public Action? OnQuery { get; set; }
        public bool EmptyFirstPage { get; set; }
        public Task<HistoryMessagePage> GetPageAsync(long? chatId, int pageSize, string? continuationToken,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("Full-chat scans are not allowed.");
        public Task<HistoryMessagePage> GetRangePageAsync(long chatId, DateTimeOffset from, DateTimeOffset to,
            string? continuationToken = null,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(-100, chatId);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), from);
            Assert.Equal(from.AddHours(1), to);
            Assert.True(cancellationToken.CanBeCanceled);
            PageCalls++;
            OnQuery?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail)
                throw new InvalidOperationException("History unavailable");
            if (EmptyFirstPage && PageCalls == 1)
                return Task.FromResult(new HistoryMessagePage([], "0"));
            var offset = continuationToken is null ? 0 : int.Parse(continuationToken);
            var items = Messages.Skip(offset).Take(7).ToArray();
            var next = offset + items.Length;
            return Task.FromResult(new HistoryMessagePage(items, next < Messages.Count ? next.ToString() : null));
        }
        public Task SaveAsync(HistoryMessage message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<HistoryMessage> GetAsync(long chatId, DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> TrimAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient telegramHttp;
        private readonly HttpClient aiHttp;
        private readonly ServiceProvider services;
        public TelegramHandler Telegram { get; } = new();
        public AiHandler Ai { get; } = new();
        public CallsStub Calls { get; } = new();
        public HistoryStub History { get; } = new();
        public ICommandProcessor Processor => services.GetRequiredService<ICommandProcessor>();
        public IMentionService Service => services.GetRequiredService<IMentionService>();
        public Update Update { get; } = new()
        {
            Message = new Message { Id = 1000, Chat = new Chat { Id = -100 }, Text = "@TestBot Question",
                Date = new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc) }
        };

        public Fixture()
        {
            telegramHttp = new HttpClient(Telegram);
            aiHttp = new HttpClient(Ai);
            var client = new ResponsesClient(new ApiKeyCredential("test-only"), new ResponsesClientOptions
            {
                Endpoint = new Uri("https://mention.example.test"), Transport = new HttpClientPipelineTransport(aiHttp),
                RetryPolicy = TelegramAntiSpamBot.OpenAI.DependencyInjection.CreateMentionClientOptions(new Uri("https://mention.example.test")).RetryPolicy
            });
            var collection = new ServiceCollection().AddLogging().AddOpenAiService().AddCommands()
                .AddSingleton<ITelegramBotClient>(new TelegramBotClient("123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk", telegramHttp))
                .AddSingleton<ICommandCallsRepository>(Calls)
                .AddSingleton<IMessageHistoryRepository>(History)
                .AddKeyedSingleton<ResponsesClient>("Mention", client);
            collection.AddSingleton<IOptions<OpenAiServicesConfiguration>>(Options.Create(new OpenAiServicesConfiguration
            {
                ServiceUrl = "https://mention.example.test/", ImageRecognitionDeployment = "images",
                SpamRecognitionDeployment = "antispam", MentionDeployment = "mention"
            }));
            collection.AddSingleton<IOptions<TelegramBotConfiguration>>(Options.Create(new TelegramBotConfiguration
            {
                Token = "test", SecretHeader = "test", BotName = "@TestBot", DebugAiResponse = false
            }));
            foreach (var descriptor in collection.Where(descriptor =>
                         descriptor.ServiceType.Name == "ICommand" && descriptor.ImplementationType?.Name != "MentionCommand").ToArray())
                collection.Remove(descriptor);
            services = collection.BuildServiceProvider();
        }

        public void Dispose()
        {
            services.Dispose();
            telegramHttp.Dispose();
            aiHttp.Dispose();
        }
    }

    private sealed class CallsStub : ICommandCallsRepository
    {
        public bool Allowed { get; set; } = true;
        public int Count { get; private set; }
        public string? Command { get; private set; }
        public TimeSpan Cooldown { get; private set; }
        public Task<bool> TryAcquireAsync(long chatId, string command, DateTimeOffset now, TimeSpan cooldown,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(-100, chatId);
            Count++;
            Command = command;
            Cooldown = cooldown;
            return Task.FromResult(Allowed);
        }
        public Task<CommandCallPage> GetPageAsync(long? chatId, int pageSize, string? continuationToken,
            CancellationToken cancellationToken = default) => Task.FromResult(new CommandCallPage([], null));
    }

    private sealed class AiHandler : HttpMessageHandler
    {
        public MentionRequest? Input { get; private set; }
        public int Requests { get; private set; }
        public string Prompt { get; private set; } = "";
        public string Answer { get; set; } = "Answer";
        public bool Fail { get; set; }
        public int RateLimitResponses { get; set; }
        public bool Search { get; set; }
        public string[] SourceUrls { get; set; } = ["https://example.com/source"];
        public object[]? Annotations { get; set; }
        public string Status { get; set; } = "completed";
        public int HistoryCalls { get; set; }
        public List<JsonElement> HistoryResults { get; } = [];
        public List<string> HistoryErrors { get; } = [];
        public bool ExpectHistoryError { get; set; }
        public string HistoryFrom { get; set; } = "2026-01-01T00:00:00+00:00";
        public string HistoryTo { get; set; } = "2026-01-01T01:00:00+00:00";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.EndsWith("/responses", request.RequestUri!.AbsolutePath);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            string Text(JsonElement message)
            {
                var content = message.GetProperty("content");
                return content.ValueKind == JsonValueKind.String ? content.GetString()! : content[0].GetProperty("text").GetString()!;
            }
            var root = document.RootElement;
            Assert.Equal("mention", root.GetProperty("model").GetString());
            Assert.False(root.GetProperty("store").GetBoolean());
            Assert.Equal(3, root.GetProperty("max_tool_calls").GetInt32());
            Assert.Equal(2000, root.GetProperty("max_output_tokens").GetInt32());
            var tools = root.GetProperty("tools").EnumerateArray().ToArray();
            Assert.Single(tools, tool => tool.GetProperty("type").GetString() == "web_search");
            var messages = root.GetProperty("input");
            var user = Assert.Single(messages.EnumerateArray(), message =>
                message.TryGetProperty("role", out var role) && role.GetString() == "user");
            Assert.False(root.TryGetProperty("previous_response_id", out _));
            Prompt = root.TryGetProperty("instructions", out var instructions) ? instructions.GetString()! : Text(messages[0]);
            Input = JsonSerializer.Deserialize<MentionRequest>(Text(user));
            using var payload = JsonDocument.Parse(Text(user));
            Assert.False(payload.RootElement.TryGetProperty("Context", out _));
            Assert.Equal(Input!.Conversation is not null,
                tools.Any(tool => tool.GetProperty("type").GetString() == "function" &&
                    tool.GetProperty("name").GetString() == "get_message_history"));
            if (Input.Conversation is not null)
            {
                var schema = Assert.Single(tools, tool => tool.GetProperty("type").GetString() == "function")
                    .GetProperty("parameters").GetProperty("properties");
                Assert.Equal("date-time", schema.GetProperty("from").GetProperty("format").GetString());
                Assert.Equal("date-time", schema.GetProperty("to").GetProperty("format").GetString());
                Assert.False(schema.TryGetProperty("chatId", out _));
            }
            var results = messages.EnumerateArray().Where(message =>
                message.TryGetProperty("type", out var type) && type.GetString() == "function_call_output").ToArray();
            HistoryResults.Clear();
            HistoryErrors.Clear();
            foreach (var result in results)
            {
                if (ExpectHistoryError)
                {
                    HistoryErrors.Add(result.GetProperty("output").GetString()!);
                    continue;
                }
                using var outputDocument = JsonDocument.Parse(result.GetProperty("output").GetString()!);
                HistoryResults.Add(outputDocument.RootElement.Clone());
            }
            if (Requests <= RateLimitResponses)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("{\"error\":{\"message\":\"Token rate limit exceeded\",\"type\":\"rate_limit_exceeded\"}}",
                        Encoding.UTF8, "application/json")
                };
                response.Headers.Add("retry-after-ms", "1");
                return response;
            }
            var output = new List<object>();
            if (results.Length < HistoryCalls)
            {
                output.Add(new { type = "function_call", id = "fc-" + results.Length,
                    call_id = "call-" + results.Length, name = "get_message_history", status = "completed",
                    arguments = JsonSerializer.Serialize(new
                    {
                        from = HistoryFrom, to = HistoryTo,
                        continuationToken = HistoryResults.Count == 0 ? null : HistoryResults[^1].GetProperty("ContinuationToken").GetString()
                    }) });
            }
            else
            {
            if (Search)
                output.Add(new { type = "web_search_call", id = "ws-test", status = "completed",
                    action = new { type = "search", query = "public question" } });
            output.Add(new { type = "message", id = "msg-test", status = "completed", role = "assistant",
                content = new[] { new { type = "output_text", text = Answer, annotations = Search
                    ? Annotations ?? SourceUrls.Select(url => (object)new { type = "url_citation", start_index = 0, end_index = 6,
                        title = "Official source", url }).ToArray()
                    : Array.Empty<object>() } } });
            }
            var json = Fail ? "{\"error\":{\"message\":\"AI unavailable\",\"type\":\"invalid_request_error\"}}" : JsonSerializer.Serialize(new
            {
                id = "resp-test", @object = "response", created_at = 0, model = "mention", status = Status,
                incomplete_details = Status == "incomplete" ? new { reason = "max_output_tokens" } : null,
                output
            });
            return new HttpResponseMessage(Fail ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TelegramHandler : HttpMessageHandler
    {
        public List<int> Deleted { get; } = [];
        public List<JsonElement> Sent { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (request.RequestUri!.AbsolutePath.EndsWith("/deleteMessage", StringComparison.OrdinalIgnoreCase))
                Deleted.Add(document.RootElement.GetProperty("message_id").GetInt32());
            else
            {
                Assert.EndsWith("/sendMessage", request.RequestUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
                Sent.Add(document.RootElement.Clone());
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":99,\"date\":0,\"chat\":{\"id\":-100,\"type\":\"supergroup\"}}}", Encoding.UTF8, "application/json")
            };
        }
    }
}
