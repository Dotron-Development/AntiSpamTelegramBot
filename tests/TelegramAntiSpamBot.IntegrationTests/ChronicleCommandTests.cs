using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramAntiSpamBot.Commands;
using TelegramAntiSpamBot.OpenAI;
using TelegramAntiSpamBot.Persistence.DTO;
using TelegramAntiSpamBot.Persistence.Repositories;
using TelegramAntispamBot.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Chat;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;

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
    public async Task ReadsCurrentChatHourAndSummarizesOnlyLatestHundred(int count, int expected, int firstId)
    {
        using var fixture = new Fixture();
        for (var id = 1; id <= count; id++)
            fixture.History.Messages.Add(new(-100, id, DateTime.UtcNow.AddMinutes(-1).Ticks, new(null, null, "Discussion")));
        fixture.History.Messages.Add(new(-100, 1000, DateTime.UtcNow.Ticks, new(null, null, "/chronicle")));
        var earliest = DateTimeOffset.UtcNow;

        await fixture.ProcessAsync();

        Assert.Equal("/chronicle", fixture.Command.GetType().GetProperty("Command")!.GetValue(fixture.Command));
        Assert.Equal(-100, fixture.History.ChatId);
        Assert.Equal(TimeSpan.FromHours(1), fixture.History.To - fixture.History.From);
        Assert.InRange(fixture.History.To, earliest, DateTimeOffset.UtcNow);
        Assert.Equal(expected, fixture.Ai.Messages!.Count);
        Assert.Equal(firstId, fixture.Ai.Messages[0].MessageId);
        Assert.Equal(count, fixture.Ai.Messages[^1].MessageId);
        Assert.Equal(1, fixture.Ai.Requests);
        Assert.Contains("same language", fixture.Ai.Prompt);
        Assert.Contains("about 200 words", fixture.Ai.Prompt);
        Assert.Equal("Summary", Assert.Single(fixture.Telegram.Texts));
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
    public async Task AiFailureSendsReadableError()
    {
        using var fixture = new Fixture();
        fixture.History.Messages.Add(new(-100, 6, DateTime.UtcNow.Ticks, new(null, null, "Discussion")));
        fixture.Ai.Fail = true;
        await fixture.ProcessAsync();
        Assert.Contains("Unable to generate", Assert.Single(fixture.Telegram.Texts));
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
        Assert.Equal(TimeSpan.FromSeconds(seconds), fixture.Calls.Cooldown);
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
            CancellationToken cancellationToken = default) => Task.FromResult(new HistoryMessagePage([], null));
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
            foreach (var message in Messages)
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
                JsonSerializer.Deserialize<HistoryMessageContent>(row.GetProperty("Content"))!)).ToArray();
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