namespace TelegramAntiSpamBot.IntegrationTests;

public class MentionCommandTests
{
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
        var text = Assert.Single(fixture.Telegram.Sent).GetProperty("text").GetString()!;
        Assert.InRange(text.Length, 1, 4096);
        Assert.StartsWith("*", text);
        Assert.EndsWith("\\!*", text);
    }

    [Theory]
    [InlineData(3996)]
    [InlineData(3997)]
    public async Task SharedTruncationNeverSplitsSurrogatePairs(int prefixLength)
    {
        using var fixture = new Fixture();
        fixture.Ai.Answer = new string('a', prefixLength) + char.ConvertFromUtf32(0x1F600) + new string('b', 200);
        var answer = await fixture.Service.GenerateAsync(new("Question", null, null));
        Assert.InRange(answer.Length, 1, 4000);
        Assert.EndsWith("...", answer);
        Assert.DoesNotContain('\uFFFD', Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(answer)));
    }

    [Fact]
    public async Task SharedTruncationPreservesExactLimit()
    {
        using var fixture = new Fixture();
        fixture.Ai.Answer = new string('a', 4000);
        Assert.Equal(fixture.Ai.Answer, await fixture.Service.GenerateAsync(new("Question", null, null)));
    }

    [Fact]
    public async Task WithoutReplySendsTenLatestMessagesBeforeQuestionAcrossPages()
    {
        using var fixture = new Fixture();
        fixture.History.Messages.AddRange(Enumerable.Range(980, 23).Select(History));
        fixture.History.Messages.Add(History(999) with { ChatId = -200 });
        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Equal(Enumerable.Range(990, 10), fixture.Ai.Input!.Context!.Select(item => item.MessageId));
        Assert.True(fixture.History.PageCalls > 1);
    }

    [Fact]
    public async Task ReplySendsTenMessagesOnEachSideWithoutTargetQuestionOrFutureMessages()
    {
        using var fixture = new Fixture();
        fixture.Update.Message!.ReplyToMessage = new Message { Id = 980, Text = "Target" };
        fixture.History.Messages.AddRange(Enumerable.Range(965, 40).Select(History));
        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Equal(Enumerable.Range(970, 10).Concat(Enumerable.Range(981, 10)),
            fixture.Ai.Input!.Context!.Select(item => item.MessageId));
        Assert.Equal("Target", fixture.Ai.Input.ReplyTo!.Text);
        Assert.Contains("Focus specifically on Question and its explicit ReplyTo", fixture.Ai.Prompt);
        Assert.Contains("Nearby messages may be entirely unrelated", fixture.Ai.Prompt);
    }

    [Fact]
    public async Task ReplyUsesIndependentLimitsEvenWhenTargetIsMissingFromHistory()
    {
        using var fixture = new Fixture();
        fixture.Update.Message!.ReplyToMessage = new Message { Id = 997, Text = "Target" };
        fixture.History.Messages.AddRange(Enumerable.Range(980, 17).Select(History));
        fixture.History.Messages.AddRange(Enumerable.Range(998, 2).Select(History));
        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Equal(Enumerable.Range(987, 10).Concat(Enumerable.Range(998, 2)),
            fixture.Ai.Input!.Context!.Select(item => item.MessageId));
    }

    [Fact]
    public async Task ContextUsesLatestHistoryMessagesAndPreservesPayloadAndMetadata()
    {
        using var fixture = new Fixture();
        var author = new HistoryMessageAuthor(2, "Boris", "boris");
        var reply = new HistoryMessageReply(40, author, "Nested reply");
        fixture.History.Messages.Add(History(998) with
        {
            UpdateId = 20, UpdateType = HistoryUpdateType.EditedMessage,
            Content = new(author, reply, "Updated caption")
        });
        fixture.History.Messages.Add(History(998) with { UpdateId = 10 });
        fixture.History.Messages.Add(History(999) with
        {
            Content = new(null, null, new MessagePayload(ImageDescription: "Image only", ImageText: "Extracted text"))
        });
        fixture.History.Messages.Add(History(997) with { Content = new(null, null, new MessagePayload()) });
        await fixture.Processor.ProcessCommand(fixture.Update);

        var context = fixture.Ai.Input!.Context!;
        Assert.Equal(new[] { 998, 999 }, context.Select(item => item.MessageId));
        Assert.Equal(fixture.History.Messages[0], context[0]);
        Assert.Equal(fixture.History.Messages[2], context[1]);
        Assert.Equal(author, context[0].Content.From);
        Assert.Equal(reply, context[0].Content.ReplyTo);
        Assert.Equal("Updated caption", context[0].Content.Payload.UserMessage);
        Assert.Equal(HistoryUpdateType.EditedMessage, context[0].UpdateType);
        Assert.Equal(20, context[0].UpdateId);
        Assert.Equal("Image only", context[1].Content.Payload.ImageDescription);
        Assert.Equal("Extracted text", context[1].Content.Payload.ImageText);
    }

    [Fact]
    public async Task ContextPreservesFullTextAndNestedReply()
    {
        using var fixture = new Fixture();
        var text = new string('a', 3996) + "😀" + new string('b', 100);
        var replyText = new string('a', 1996) + "😀" + new string('b', 100);
        fixture.History.Messages.Add(History(999) with
        {
            Content = new(null, new HistoryMessageReply(42, null, replyText), text)
        });
        await fixture.Processor.ProcessCommand(fixture.Update);

        var context = Assert.Single(fixture.Ai.Input!.Context!);
        Assert.Equal(text, context.Content.Payload.UserMessage);
        Assert.Equal(replyText, context.Content.ReplyTo!.Text);
    }

    [Fact]
    public async Task HistoryFailureStillAnswersQuestionAndReplyWithoutContext()
    {
        using var fixture = new Fixture();
        fixture.History.Fail = true;
        fixture.Update.Message!.ReplyToMessage = new Message { Id = 42, Text = "Target" };
        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Empty(fixture.Ai.Input!.Context!);
        Assert.Equal("Question", fixture.Ai.Input.Question);
        Assert.Equal("Target", fixture.Ai.Input.ReplyTo!.Text);
        Assert.Single(fixture.Telegram.Sent);
    }

    private static HistoryMessage History(int id) => new(-100, id,
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(id).Ticks,
        new(null, null, "Message " + id));

    private sealed class HistoryStub : IMessageHistoryRepository
    {
        public List<HistoryMessage> Messages { get; } = [];
        public int PageCalls { get; private set; }
        public bool Fail { get; set; }
        public Task<HistoryMessagePage> GetPageAsync(long? chatId, int pageSize, string? continuationToken,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(-100, chatId);
            PageCalls++;
            if (Fail)
                throw new InvalidOperationException("History unavailable");
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
            Message = new Message { Id = 1000, Chat = new Chat { Id = -100 }, Text = "@TestBot Question" }
        };

        public Fixture()
        {
            telegramHttp = new HttpClient(Telegram);
            aiHttp = new HttpClient(Ai);
            var client = new ChatClient("mention", new ApiKeyCredential("test-only"), new OpenAIClientOptions
            {
                Endpoint = new Uri("https://mention.example.test"), Transport = new HttpClientPipelineTransport(aiHttp)
            });
            var collection = new ServiceCollection().AddLogging().AddOpenAiService().AddCommands()
                .AddSingleton<ITelegramBotClient>(new TelegramBotClient("123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk", telegramHttp))
                .AddSingleton<ICommandCallsRepository>(Calls)
                .AddSingleton<IMessageHistoryRepository>(History)
                .AddKeyedSingleton<ChatClient>("Mention", client);
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
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Contains("mention", request.RequestUri!.AbsoluteUri);
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            string Text(JsonElement message)
            {
                var content = message.GetProperty("content");
                return content.ValueKind == JsonValueKind.String ? content.GetString()! : content[0].GetProperty("text").GetString()!;
            }
            var messages = document.RootElement.GetProperty("messages");
            Prompt = Text(messages[0]);
            Input = JsonSerializer.Deserialize<MentionRequest>(Text(messages[1]));
            var json = Fail ? "{\"error\":{\"message\":\"AI unavailable\",\"type\":\"invalid_request_error\"}}" : JsonSerializer.Serialize(new
            {
                id = "chatcmpl-test", @object = "chat.completion", created = 0, model = "mention",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = Answer }, finish_reason = "stop" } }
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
