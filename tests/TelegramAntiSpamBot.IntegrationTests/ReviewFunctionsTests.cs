using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using TelegramAntispamBot.Configuration;
using TelegramAntiSpamBot.Commands;
using TelegramAntiSpamBot.Functions;
using TelegramAntiSpamBot.OpenAI;
using TelegramAntiSpamBot.Persistence;
using TelegramAntiSpamBot.Persistence.DTO;

namespace TelegramAntiSpamBot.IntegrationTests;

public class ReviewFunctionsTests
{
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
            Assert.Equal("from: Alice\nOriginal caption\nRecognized text", fixture.Telegram.Calls[0].Body.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ReviewPreservesLongUnicodeTextAcrossTelegramMessages()
    {
        using var fixture = new Fixture();
        var original = new string('a', 4083) + char.ConvertFromUtf32(0x1F600) + new string('b', 4200);
        fixture.Message = fixture.Message! with { Message = original, ImageText = string.Empty };

        await fixture.Functions.Review(fixture.Request("{\"isSpam\":false}"), -100, 7);

        var chunks = fixture.Telegram.Calls.Select(call => call.Body.GetProperty("text").GetString()!).ToArray();
        Assert.Equal("from: Alice\n" + original, string.Concat(chunks));
        Assert.All(chunks, chunk =>
        {
            Assert.InRange(chunk.Length, 1, 4096);
            Assert.False(char.IsHighSurrogate(chunk[^1]));
            Assert.False(char.IsLowSurrogate(chunk[0]));
        });
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
    [InlineData("")]
    [InlineData("?chatId=0")]
    [InlineData("?chatId=-100&pageSize=0")]
    [InlineData("?chatId=-100&pageSize=1001")]
    public async Task ListRejectsMissingChatOrInvalidPageSize(string query)
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
    public void AllReviewEndpointsRequireFunctionKeys(string methodName)
    {
        var trigger = typeof(ReviewFunctions).GetMethod(methodName)!.GetParameters()[0]
            .GetCustomAttribute<HttpTriggerAttribute>();
        Assert.Equal(AuthorizationLevel.Function, trigger!.AuthLevel);
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
        Assert.Equal("Alice Smith", message.UserName);
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
        "from":{"id":42,"is_bot":false,"first_name":"Alice","last_name":"Smith"},
        "chat":{"id":-100,"type":"supergroup","title":"Test"},
        "caption":"Original Caption 123!",
        "photo":[{"file_id":"photo","file_unique_id":"unique","width":100,"height":100}]}}
        """;

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider services = new ServiceCollection()
            .AddSingleton(Options.Create(new WorkerOptions
            {
                Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web))
            })).BuildServiceProvider();
        private readonly HttpClient httpClient;
        public TelegramHandler Telegram { get; } = new();
        public ReviewMessage? Message { get; set; } = new(-100, 7, 42, "Alice", "Original caption", "Recognized text", "HASH", 638000000000000000);
        public List<(string Hash, bool IsSpam)> HashDecisions { get; } = [];
        public bool Deleted { get; private set; }
        public (long ChatId, int PageSize, string? Token)? Query { get; private set; }
        public ReviewFunctions Functions { get; }
        public AntiSpamBotWebHook Webhook { get; }
        public DetectorStub Detector { get; } = new();
        public ShortcutCheckResult Shortcut { get; set; } = ShortcutCheckResult.NotFound;
        public ReviewMessage? SavedMessage { get; private set; }
        public List<ReviewChat> SavedChats { get; } = [];
        public bool ChatSaveFails { get; set; }
        public (int PageSize, string? Token)? ChatQuery { get; private set; }

        public Fixture()
        {
            var repository = DispatchProxy.Create<IAntiSpamBotRepository, RepositoryProxy>();
            ((RepositoryProxy)repository).Handler = (method, arguments) =>
            {
                switch (method.Name)
                {
                    case nameof(IAntiSpamBotRepository.SaveChatAsync):
                        if (ChatSaveFails) return Task.FromException(new InvalidOperationException("Storage unavailable"));
                        SavedChats.Add((ReviewChat)arguments[0]!);
                        return Task.CompletedTask;
                    case nameof(IAntiSpamBotRepository.GetReviewChatsAsync):
                        ChatQuery = ((int)arguments[0]!, (string?)arguments[1]);
                        return Task.FromResult(new ReviewChatPage([new ReviewChat(-100, "Test", DateTimeOffset.UtcNow)], "next-chat"));
                    case nameof(IAntiSpamBotRepository.GetUserMessageCount):
                        return Task.FromResult(0);
                    case nameof(IAntiSpamBotRepository.IncreaseMessageCount):
                    case nameof(IAntiSpamBotRepository.UpdateChannelSpamStats):
                        return Task.CompletedTask;
                    case nameof(IAntiSpamBotRepository.IsSpam):
                        return Task.FromResult(Shortcut);
                    case nameof(IAntiSpamBotRepository.SaveReviewMessageAsync):
                        SavedMessage = (ReviewMessage)arguments[0]!;
                        return Task.CompletedTask;
                    case nameof(IAntiSpamBotRepository.GetReviewMessageAsync):
                        Assert.Equal(-100L, arguments[0]);
                        Assert.Equal(7, arguments[1]);
                        return Task.FromResult(Message);
                    case nameof(IAntiSpamBotRepository.GetReviewMessagesAsync):
                        Query = ((long)arguments[0]!, (int)arguments[1]!, (string?)arguments[2]);
                        return Task.FromResult(new ReviewMessagePage([Message!], "next"));
                    case nameof(IAntiSpamBotRepository.SaveMessageHash):
                        HashDecisions.Add(((string)arguments[0]!, (bool)arguments[1]!));
                        return Task.CompletedTask;
                    case nameof(IAntiSpamBotRepository.DeleteReviewMessageAsync):
                        Deleted = true;
                        return Task.CompletedTask;
                    default:
                        throw new InvalidOperationException(method.Name);
                }
            };
            httpClient = new HttpClient(Telegram);
            const string token = "123456789:ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk";
            var bot = new TelegramBotClient(token, httpClient);
            Functions = new ReviewFunctions(repository, bot);
            Webhook = new AntiSpamBotWebHook(NullLogger<AntiSpamBotWebHook>.Instance,
                Detector, repository, bot, new TelegramImageUrlResolver(token), new CommandStub(),
                Options.Create(new TelegramBotConfiguration
                {
                    Token = token,
                    SecretHeader = "test",
                    BotName = "@TestBot",
                    DebugAiResponse = false
                }));
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
                _ => "{\"message_id\":99,\"date\":0,\"chat\":{\"id\":-100,\"type\":\"supergroup\"}}"
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