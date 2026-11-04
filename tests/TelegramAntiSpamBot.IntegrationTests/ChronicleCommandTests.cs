
namespace TelegramAntiSpamBot.IntegrationTests;

public class ChronicleCommandTests
{
    [Theory]
    [InlineData("/chronicle")]
    [InlineData("/CHRONICLE@TestBot")]
    public async Task ProcessorDeletesInvocationBeforeReadingHistory(string text)
    {
        using var fixture = new Fixture();
        fixture.Update.Message!.Text = text;
        fixture.Telegram.DeleteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var processing = fixture.Processor.ProcessCommand(fixture.Update);
        await fixture.Telegram.DeleteStarted.Task;

        Assert.False(processing.IsCompleted);
        Assert.Equal(0, fixture.Calls.Count);
        Assert.Equal(default, fixture.History.To);
        Assert.Empty(fixture.Telegram.Texts);
        fixture.Telegram.DeleteGate.SetResult();
        await processing;

        Assert.Equal(1000, Assert.Single(fixture.Telegram.DeletedMessageIds));
        Assert.Equal(1, fixture.Calls.Count);
        Assert.Equal("No messages in the last hour.", Assert.Single(fixture.Telegram.Texts));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingCommandMessageDoesNotPreventCooldownCheck(bool allowed)
    {
        using var fixture = new Fixture();
        fixture.Calls.Allowed = allowed;
        fixture.Telegram.DeleteErrorCode = 400;
        fixture.Telegram.DeleteErrorDescription = "Bad Request: message to delete not found";

        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Equal(1000, Assert.Single(fixture.Telegram.DeletedMessageIds));
        Assert.Equal(1, fixture.Calls.Count);
        if (allowed)
            Assert.Equal("No messages in the last hour.", Assert.Single(fixture.Telegram.Texts));
        else
            Assert.Empty(fixture.Telegram.Texts);
    }

    [Theory]
    [InlineData(400, "Bad Request: message can't be deleted")]
    [InlineData(403, "Forbidden: bot was kicked from the supergroup chat")]
    [InlineData(404, "Bad Request: message to delete not found")]
    public async Task OtherDeletionErrorsPropagateWithoutExecutingCommand(int errorCode, string description)
    {
        using var fixture = new Fixture();
        fixture.Telegram.DeleteErrorCode = errorCode;
        fixture.Telegram.DeleteErrorDescription = description;

        var exception = await Assert.ThrowsAsync<Telegram.Bot.Exceptions.ApiRequestException>(() =>
            fixture.Processor.ProcessCommand(fixture.Update));

        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.Equal(description, exception.Message);
        Assert.Equal(0, fixture.Calls.Count);
        Assert.Empty(fixture.Telegram.Texts);
        Assert.Equal(0, fixture.History.PageReads);
    }

    [Theory]
    [InlineData("/chronicle")]
    [InlineData("/CHRONICLE@TestBot")]
    public async Task CoolingDownCommandIsDeletedAndSkippedWithoutReply(string text)
    {
        using var fixture = new Fixture();
        fixture.Update.Message!.Text = text;
        fixture.Calls.Allowed = false;
        await fixture.Processor.ProcessCommand(fixture.Update);
        Assert.Equal(1000, Assert.Single(fixture.Telegram.DeletedMessageIds));
        Assert.Equal(1, fixture.Calls.Count);
        Assert.Equal(-100, fixture.Calls.ChatId);
        Assert.Equal("/chronicle", fixture.Calls.Command);
        Assert.Empty(fixture.Telegram.Texts);
        Assert.Equal(default, fixture.History.To);
        Assert.Null(fixture.Ai.Messages);
    }

    [Theory]
    [InlineData("ordinary message")]
    [InlineData("/chronicle@OtherBot")]
    [InlineData(null)]
    public async Task ProcessorDoesNotDeleteUnrecognizedMessages(string? text)
    {
        using var fixture = new Fixture();
        fixture.Update.Message!.Text = text;

        await fixture.Processor.ProcessCommand(fixture.Update);

        Assert.Empty(fixture.Telegram.DeletedMessageIds);
        Assert.Equal(0, fixture.Calls.Count);
        Assert.Empty(fixture.Telegram.Texts);
        Assert.Equal(default, fixture.History.To);
    }

    [Theory]
    [InlineData(10, 10, 1)]
    [InlineData(100, 100, 1)]
    [InlineData(150, 100, 51)]
    public async Task ReadsHourEndingAtLatestDiscussionAndSummarizesOnlyLatestHundred(int count, int expected, int firstId)
    {
        using var fixture = new Fixture();
        var latest = DateTimeOffset.UtcNow.AddDays(-3);
        for (var id = 1; id <= count; id++)
            fixture.History.Messages.Add(new(-100, id, latest.UtcTicks, new(null, null, "Discussion")));
        fixture.History.Messages.Add(new(-100, 1000, DateTime.UtcNow.Ticks, new(null, null, "/chronicle")));

        await fixture.ProcessAsync();

        Assert.Equal("/chronicle", fixture.Command.GetType().GetProperty("Command")!.GetValue(fixture.Command));
        Assert.Equal(-100, fixture.History.ChatId);
        Assert.Equal(1, fixture.History.PageReads);
        Assert.Equal(default, fixture.History.To);
        Assert.Equal(expected, fixture.Ai.Messages!.Count);
        Assert.All(fixture.Ai.Messages, message => Assert.Equal(latest.UtcTicks, message.DateTimeTicks));
        Assert.Equal(firstId, fixture.Ai.Messages[0].MessageId);
        Assert.Equal(count, fixture.Ai.Messages[^1].MessageId);
        Assert.Equal(1, fixture.Ai.Requests);
        Assert.Contains("Use the predominant language of substantive discussion", fixture.Ai.Prompt);
        Assert.Contains("ENTIRE output must stay under 4000 characters", fixture.Ai.Prompt);
        Assert.Equal("Summary", Assert.Single(fixture.Telegram.Texts));
    }

    [Theory]
    [InlineData("/chronicle")]
    [InlineData("/CHRONICLE@TestBot")]
    [InlineData("Please run /chronicle for this chat")]
    public async Task ChronicleTextIsExcludedFromWindowAndSummary(string command)
    {
        using var fixture = new Fixture();
        var latest = DateTimeOffset.UtcNow.AddDays(-3);
        fixture.History.Messages.Add(new(-100, 1, latest.AddMinutes(-10).UtcTicks, new(null, null, command)));
        fixture.History.Messages.Add(new(-100, 1000, latest.UtcTicks, new(null, null, "Discussion")));
        fixture.History.Messages.Add(new(-100, 2, DateTimeOffset.UtcNow.UtcTicks, new(null, null, command)));

        await fixture.ProcessAsync();

        Assert.Equal(default, fixture.History.To);
        Assert.Equal(1000, Assert.Single(fixture.Ai.Messages!).MessageId);
    }

    [Fact]
    public async Task HistoryContainingOnlyChronicleCommandsSkipsAi()
    {
        using var fixture = new Fixture();
        fixture.History.Messages.Add(new(-100, 1, DateTimeOffset.UtcNow.UtcTicks,
            new(null, null, "/CHRONICLE@TestBot")));

        await fixture.ProcessAsync();

        Assert.Null(fixture.Ai.Messages);
        Assert.Equal("No messages in the last hour.", Assert.Single(fixture.Telegram.Texts));
    }

    [Fact]
    public async Task ChronicleReceivesEveryVersionWithUpdateTypeAndTypedPayload()
    {
        using var fixture = new Fixture();
        var latest = DateTimeOffset.UtcNow.AddDays(-3);
        var original = new HistoryMessage(-100, 7, latest.UtcTicks,
            new(null, null, new MessagePayload("A poster", "Full image text", "Original caption")));
        var edited = original with
        {
            DateTimeTicks = latest.AddMinutes(1).UtcTicks,
            UpdateType = HistoryUpdateType.EditedMessage,
            Content = original.Content with { Payload = original.Content.Payload with { UserMessage = "Edited caption" } }
        };
        fixture.History.Messages.AddRange([original, edited]);

        await fixture.ProcessAsync();

        Assert.Equal(new[] { original, edited }, fixture.Ai.Messages);
        Assert.Contains("versions of one message", fixture.Ai.Prompt);
        Assert.Equal(1, fixture.Ai.Requests);
    }

    [Fact]
    public async Task DirectSummaryAcceptsFilteredMessagesAndSkipsEmptyInput()
    {
        using var fixture = new Fixture();
        var messages = new HistoryMessage[]
        {
            new(-100, 2, DateTimeOffset.UtcNow.UtcTicks, new(null, null, "Discussion"))
        };

        await fixture.Chronicle.SummarizeAsync(messages);

        Assert.Equal(2, Assert.Single(fixture.Ai.Messages!).MessageId);
        Assert.Equal("No messages in the last hour.", await fixture.Chronicle.SummarizeAsync([]));
        Assert.Equal(1, fixture.Ai.Requests);
    }

    [Fact]
    public async Task EmptyHistorySkipsAi()
    {
        using var fixture = new Fixture();
        await fixture.ProcessAsync();
        Assert.Null(fixture.Ai.Messages);
        Assert.Equal("No messages in the last hour.", Assert.Single(fixture.Telegram.Texts));
    }

    [Fact]
    public async Task AiFailureDoesNotSendAnything()
    {
        using var fixture = new Fixture();
        fixture.History.Messages.Add(new(-100, 6, DateTime.UtcNow.Ticks, new(null, null, "Discussion")));
        fixture.Ai.Fail = true;
        await fixture.ProcessAsync();
        Assert.Empty(fixture.Telegram.Texts);
    }

    [Fact]
    public async Task EmptyAiResponseDoesNotSendAnything()
    {
        using var fixture = new Fixture();
        fixture.History.Messages.Add(new(-100, 6, DateTime.UtcNow.Ticks, new(null, null, "Discussion")));
        fixture.Ai.Summary = "";
        await fixture.ProcessAsync();
        Assert.Empty(fixture.Telegram.Texts);
    }

    [Fact]
    public async Task LongSummaryFitsOneMessageWithoutBreakingUnicode()
    {
        using var fixture = new Fixture();
        fixture.History.Messages.Add(new(-100, 6, DateTime.UtcNow.Ticks, new(null, null, "Discussion")));
        fixture.Ai.Summary = new string('a', 3996) + char.ConvertFromUtf32(0x1F600) + new string('b', 200);
        await fixture.ProcessAsync();
        var text = Assert.Single(fixture.Telegram.Texts);
        Assert.Equal(new string('a', 3996) + "...", text);
        Assert.InRange(text.Length, 1, 4000);
    }

    [Fact]
    public async Task MissingHistoryTableIsTreatedAsEmpty()
    {
        using var fixture = new Fixture();
        fixture.History.MissingTable = true;
        await fixture.ProcessAsync();
        Assert.Null(fixture.Ai.Messages);
        Assert.Equal("No messages in the last hour.", Assert.Single(fixture.Telegram.Texts));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(600)]
    public async Task ProcessorUsesConfiguredCooldown(int seconds)
    {
        using var fixture = new Fixture(TimeSpan.FromSeconds(seconds));
        await fixture.Processor.ProcessCommand(fixture.Update);
        Assert.Equal(1, fixture.Calls.Count);
        Assert.Equal(TimeSpan.FromSeconds(seconds), fixture.Calls.Cooldown);
    }

    [Fact]
    public async Task WindowIncludesBothBoundariesAndFindsLatestAcrossHistoryPages()
    {
        using var fixture = new Fixture();
        var latest = DateTimeOffset.UtcNow.AddDays(-7);
        for (var id = 1; id <= 1000; id++)
            fixture.History.Messages.Add(new(-100, id, latest.AddHours(-2).UtcTicks, new(null, null, "Older")));
        fixture.History.Messages.Add(new(-100, 1001, latest.AddHours(-1).UtcTicks, new(null, null, "Start")));
        fixture.History.Messages.Add(new(-100, 1002, latest.UtcTicks, new(null, null, "End")));

        await fixture.ProcessAsync();

        Assert.Equal(new[] { 1001, 1002 }, fixture.Ai.Messages!.Select(message => message.MessageId));
        Assert.Equal(latest.AddHours(-1).UtcTicks, fixture.Ai.Messages![0].DateTimeTicks);
        Assert.Equal(latest.UtcTicks, fixture.Ai.Messages[1].DateTimeTicks);
        Assert.Equal(4, fixture.History.PageReads);
        Assert.Equal(default, fixture.History.To);
    }

    [Fact]
    public async Task CommandOnlyPagesDoNotShiftWindowOrStopPagination()
    {
        using var fixture = new Fixture();
        var latest = DateTimeOffset.UtcNow.AddDays(-7);
        for (var id = 1; id <= 1000; id++)
            fixture.History.Messages.Add(new(-100, id, latest.AddHours(-3).UtcTicks, new(null, null, "/chronicle")));
        fixture.History.Messages.Add(new(-100, 1001, latest.AddHours(-1).AddTicks(-1).UtcTicks, new(null, null, "Outside")));
        fixture.History.Messages.Add(new(-100, 1002, latest.AddHours(-1).UtcTicks, new(null, null, "Start")));
        fixture.History.Messages.Add(new(-100, 1003, latest.UtcTicks, new(null, null, "End")));
        for (var id = 1004; id <= 3000; id++)
            fixture.History.Messages.Add(new(-100, id, latest.AddDays(1).UtcTicks, new(null, null, "/CHRONICLE@TestBot")));

        await fixture.ProcessAsync();

        Assert.Equal(10, fixture.History.PageReads);
        Assert.Equal(default, fixture.History.To);
        Assert.Equal(new[] { 1002, 1003 }, fixture.Ai.Messages!.Select(message => message.MessageId));
        Assert.Equal(1, fixture.Ai.Requests);
    }

    [Fact]
    public async Task ReadsRemainingPagesAfterCollectingMoreThanFiveHundredMessages()
    {
        using var fixture = new Fixture();
        var latest = DateTimeOffset.UtcNow.AddDays(-7);
        for (var id = 1; id <= 1500; id++)
            fixture.History.Messages.Add(new(-100, id, latest.UtcTicks, new(null, null, "Discussion")));

        await fixture.ProcessAsync();

        Assert.Equal(5, fixture.History.PageReads);
        Assert.Equal(Enumerable.Range(1401, 100), fixture.Ai.Messages!.Select(message => message.MessageId));
        Assert.Equal(1, fixture.Ai.Requests);
    }

    [Fact]
    public async Task DirectSummarySortsAndLimitsMessagesToLatestHundred()
    {
        using var fixture = new Fixture();
        var latest = DateTimeOffset.UtcNow.AddDays(-7);
        var messages = Enumerable.Range(1, 150).Reverse()
            .Select(id => new HistoryMessage(-100, id, latest.UtcTicks, new(null, null, "Discussion")))
            .ToArray();

        await fixture.Chronicle.SummarizeAsync(messages);

        Assert.Equal(Enumerable.Range(51, 100), fixture.Ai.Messages!.Select(message => message.MessageId));
        Assert.Equal(1, fixture.Ai.Requests);
    }

    [Fact]
    public async Task CancelledGenerationDoesNotReadHistoryOrCallAi()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Chronicle.GenerateAsync(-100, cancellation.Token));

        Assert.Equal(0, fixture.History.PageReads);
        Assert.Equal(0, fixture.Ai.Requests);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        private readonly HttpClient aiHttp;
        private readonly ServiceProvider services;
        public HistoryStub History { get; } = new();
        public AiStub Ai { get; } = new();
        public CallsStub Calls { get; } = new();
        public TelegramHandler Telegram { get; } = new();
        public object Command { get; }
        public IChronicleService Chronicle => services.GetRequiredService<IChronicleService>();
        public ICommandProcessor Processor { get; }
        public Update Update { get; } = new()
        {
            Message = new Message { Id = 1000, Chat = new Chat { Id = -100 }, Text = "/chronicle" }
        };

        public Fixture(TimeSpan? cooldown = null)
        {
            http = new HttpClient(Telegram);
            var bot = new TelegramBotClient("123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk", http);
            var type = typeof(ICommandProcessor).Assembly.GetType("TelegramAntiSpamBot.Commands.Commands.ChronicleCommand", true)!;
            var logger = Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type));
            aiHttp = new HttpClient(Ai);
            var client = new ChatClient("chronicle", new ApiKeyCredential("test-only"), new OpenAIClientOptions
            {
                Endpoint = new Uri("https://chronicle.example.test"),
                Transport = new HttpClientPipelineTransport(aiHttp)
            });
            services = new ServiceCollection().AddOpenAiService()
                .AddSingleton<IMessageHistoryRepository>(History)
                .AddKeyedSingleton<ChatClient>("Chronicle", client).BuildServiceProvider();
            Command = Activator.CreateInstance(type, bot, services.GetRequiredService<IChronicleService>(), logger)!;
            var commandInterface = type.GetInterface("ICommand")!;
            var commands = Array.CreateInstance(commandInterface, 1);
            commands.SetValue(Command, 0);
            var processorType = typeof(ICommandProcessor).Assembly.GetType("TelegramAntiSpamBot.Commands.CommandProcessor", true)!;
            var processorLogger = Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(processorType));
            var configuration = new TelegramBotConfiguration
            {
                Token = "test",
                SecretHeader = "test",
                BotName = "@TestBot",
                DebugAiResponse = false,
                CommandCooldown = cooldown ?? TimeSpan.FromMinutes(5)
            };
            Processor = (ICommandProcessor)Activator.CreateInstance(processorType, commands, configuration, bot, Calls, processorLogger)!;
        }

        public Task ProcessAsync() => (Task)Command.GetType().GetMethod("ProcessAsync")!.Invoke(Command, [Update])!;
        public void Dispose()
        {
            http.Dispose();
            aiHttp.Dispose();
            services.Dispose();
        }
    }

    private sealed class CallsStub : ICommandCallsRepository
    {
        public Task<CommandCallPage> GetPageAsync(long? chatId, int pageSize, string? continuationToken,
            CancellationToken cancellationToken = default) => Task.FromResult(new CommandCallPage([], null));
        public bool Allowed { get; set; } = true;
        public int Count { get; private set; }
        public long ChatId { get; private set; }
        public string? Command { get; private set; }
        public TimeSpan Cooldown { get; private set; }

        public Task<bool> TryAcquireAsync(long chatId, string command, DateTimeOffset now, TimeSpan cooldown,
            CancellationToken cancellationToken = default)
        {
            Count++;
            ChatId = chatId;
            Command = command;
            Cooldown = cooldown;
            return Task.FromResult(Allowed);
        }
    }

    private sealed class HistoryStub : IMessageHistoryRepository
    {
        public Task<HistoryMessagePage> GetPageAsync(long? chatId, int pageSize, string? continuationToken,
            CancellationToken cancellationToken = default)
        {
            PageReads++;
            ChatId = chatId ?? 0;
            if (MissingTable)
                throw new Azure.RequestFailedException(404, "Table not found");
            var offset = continuationToken is null ? 0 : int.Parse(continuationToken);
            var page = Messages.Where(message => chatId is null || message.ChatId == chatId)
                .OrderBy(message => message.DateTimeTicks).ThenBy(message => message.MessageId)
                .Skip(offset).Take(pageSize).ToArray();
            var nextOffset = offset + page.Length;
            return Task.FromResult(new HistoryMessagePage(page,
                nextOffset < Messages.Count(message => chatId is null || message.ChatId == chatId)
                    ? nextOffset.ToString() : null));
        }
        public int PageReads { get; private set; }
        public List<HistoryMessage> Messages { get; } = [];
        public bool MissingTable { get; set; }
        public long ChatId { get; private set; }
        public DateTimeOffset From { get; private set; }
        public DateTimeOffset To { get; private set; }
        public Task SaveAsync(HistoryMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> TrimAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public IAsyncEnumerable<HistoryMessage> GetAsync(long chatId, DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            ChatId = chatId;
            From = from;
            To = to;
            if (MissingTable)
                throw new Azure.RequestFailedException(404, "Table not found");
            return EnumerateAsync();
        }

        private async IAsyncEnumerable<HistoryMessage> EnumerateAsync()
        {
            await Task.CompletedTask;
            foreach (var message in Messages.Where(message => message.ChatId == ChatId &&
                message.DateTimeTicks >= From.UtcTicks && message.DateTimeTicks < To.UtcTicks)
                .OrderBy(message => message.DateTimeTicks).ThenBy(message => message.MessageId))
                yield return message;
        }
    }

    private sealed class AiStub : HttpMessageHandler
    {
        public IReadOnlyList<HistoryMessage>? Messages { get; private set; }
        public string Summary { get; set; } = "Summary";
        public bool Fail { get; set; }
        public int Requests { get; private set; }
        public string Prompt { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            string Text(JsonElement message)
            {
                var content = message.GetProperty("content");
                return content.ValueKind == JsonValueKind.String ? content.GetString()! : content[0].GetProperty("text").GetString()!;
            }
            Prompt = Text(document.RootElement.GetProperty("messages")[0]);
            using var payload = JsonDocument.Parse(Text(document.RootElement.GetProperty("messages")[1]));
            Messages = payload.RootElement.GetProperty("Messages").EnumerateArray().Select(row => new HistoryMessage(
                -100, row.GetProperty("MessageId").GetInt32(),
                DateTime.Parse(row.GetProperty("SentAtUtc").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind).Ticks,
                JsonSerializer.Deserialize<HistoryMessageContent>(row.GetProperty("Content"))!,
                JsonSerializer.Deserialize<HistoryUpdateType>(row.GetProperty("UpdateType")))).ToArray();
            var json = Fail ? "{\"error\":{\"message\":\"AI unavailable\",\"type\":\"invalid_request_error\"}}" : JsonSerializer.Serialize(new
            {
                id = "chatcmpl-test",
                @object = "chat.completion",
                created = 0,
                model = "chronicle",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = Summary }, finish_reason = "stop" } }
            });
            return new HttpResponseMessage(Fail ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TelegramHandler : HttpMessageHandler
    {
        public List<string> Texts { get; } = [];
        public List<int> DeletedMessageIds { get; } = [];
        public TaskCompletionSource? DeleteGate { get; set; }
        public TaskCompletionSource DeleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int? DeleteErrorCode { get; set; }
        public string? DeleteErrorDescription { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(-100, document.RootElement.GetProperty("chat_id").GetInt64());
            if (request.RequestUri!.AbsolutePath.EndsWith("/deleteMessage", StringComparison.OrdinalIgnoreCase))
            {
                DeletedMessageIds.Add(document.RootElement.GetProperty("message_id").GetInt32());
                DeleteStarted.TrySetResult();
                if (DeleteGate is not null)
                    await DeleteGate.Task.WaitAsync(cancellationToken);
                if (DeleteErrorCode is { } errorCode)
                    return new HttpResponseMessage((HttpStatusCode)errorCode)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(new
                        {
                            ok = false,
                            error_code = errorCode,
                            description = DeleteErrorDescription
                        }), Encoding.UTF8, "application/json")
                    };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":true}", Encoding.UTF8, "application/json")
                };
            }
            Texts.Add(document.RootElement.GetProperty("text").GetString()!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":99,\"date\":0,\"chat\":{\"id\":-100,\"type\":\"supergroup\"}}}", Encoding.UTF8, "application/json")
            };
        }
    }
}