namespace TelegramAntiSpamBot.IntegrationTests;

public sealed class MentionReviewFunctionsTests
{
    [Fact]
    public async Task PreviewPassesContextAndCancellationAndReturnsCamelCaseAnswer()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var response = await fixture.Functions.PreviewMention(fixture.Request("""
            {"question":"What does this mean?","fromName":"Alice","fromUsername":"alice",
             "replyTo":{"name":"Bob","username":"bob","text":"Original message"}}
            """), cancellation.Token);

        Assert.Equal(new MentionRequest("What does this mean?", new(0, "Alice", "alice"),
            new(0, new(0, "Bob", "bob"), "Original message")), Assert.Single(fixture.Service.Requests));
        Assert.Equal(cancellation.Token, fixture.Service.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        Assert.Equal("Preview answer", document.RootElement.GetProperty("answer").GetString());
        Assert.Single(document.RootElement.EnumerateObject());
    }

    [Theory]
    [InlineData("{\"question\":\"\"}")]
    [InlineData("{\"question\":\"  \"}")]
    [InlineData("{\"question\":\"Question\",\"fromName\":null,\"fromUsername\":null,\"replyTo\":null}")]
    public async Task PreviewAllowsBareMentionAndAbsentAuthorsAndReply(string body)
    {
        using var fixture = new Fixture();
        var response = await fixture.Functions.PreviewMention(fixture.Request(body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(fixture.Service.Requests);
        Assert.Null(request.From);
        Assert.Null(request.ReplyTo);
    }

    [Theory]
    [InlineData("{\"question\":\"\",\"replyTo\":{\"text\":\"\"}}", null, null)]
    [InlineData("{\"question\":\"\",\"fromUsername\":\"alice\",\"replyTo\":{\"username\":\"bob\",\"text\":\"\"}}", "alice", "bob")]
    public async Task PreviewAllowsEmptyReplyTextAndUsernameOnlyAuthors(string body, string? fromUsername, string? replyUsername)
    {
        using var fixture = new Fixture();
        Assert.Equal(HttpStatusCode.OK, (await fixture.Functions.PreviewMention(fixture.Request(body))).StatusCode);
        var request = Assert.Single(fixture.Service.Requests);
        Assert.Equal(fromUsername is null ? null : new HistoryMessageAuthor(0, "", fromUsername), request.From);
        Assert.Equal(new HistoryMessageReply(0,
            replyUsername is null ? null : new(0, "", replyUsername), ""), request.ReplyTo);
    }

    [Fact]
    public async Task PreviewAllowsNameOnlyAuthors()
    {
        using var fixture = new Fixture();
        await fixture.Functions.PreviewMention(fixture.Request("""
            {"question":"Hi","fromName":"Alice","replyTo":{"name":"Bob","text":"Hi"}}
            """));
        var request = Assert.Single(fixture.Service.Requests);
        Assert.Equal(new HistoryMessageAuthor(0, "Alice", null), request.From);
        Assert.Equal(new HistoryMessageAuthor(0, "Bob", null), request.ReplyTo!.From);
    }

    [Theory]
    [InlineData(4096, 200, 64, true)]
    [InlineData(4097, 200, 64, false)]
    [InlineData(4096, 201, 64, false)]
    [InlineData(4096, 200, 65, false)]
    public async Task PreviewValidatesEachContextFieldAtItsBoundary(int textLength, int nameLength, int usernameLength, bool valid)
    {
        using var fixture = new Fixture();
        var fields = new Dictionary<string, object?>
        {
            ["question"] = "Hi",
            ["fromName"] = "Alice",
            ["fromUsername"] = "alice",
            ["replyTo"] = new Dictionary<string, object?> { ["name"] = "Bob", ["username"] = "bob", ["text"] = "Hi" }
        };
        var reply = (Dictionary<string, object?>)fields["replyTo"]!;
        foreach (var field in new[] { "question", "fromName", "fromUsername", "text", "name", "username" })
        {
            var target = field is "text" or "name" or "username" ? reply : fields;
            var original = target[field];
            target[field] = new string('x', field is "question" or "text" ? textLength : field is "name" or "fromName" ? nameLength : usernameLength);
            var response = await fixture.Functions.PreviewMention(fixture.Request(JsonSerializer.Serialize(fields)));
            var fieldValid = field is "question" or "text" ? textLength <= 4096 : field is "name" or "fromName" ? nameLength <= 200 : usernameLength <= 64;
            Assert.Equal(fieldValid ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
            target[field] = original;
        }
        Assert.Equal(valid ? 6 : 4, fixture.Service.Requests.Count);
    }

    [Theory]
    [InlineData(2048, true)]
    [InlineData(2049, false)]
    public async Task PreviewCountsUtf16CodeUnits(int emojiCount, bool valid)
    {
        using var fixture = new Fixture();
        var question = string.Concat(Enumerable.Repeat("😀", emojiCount));
        var response = await fixture.Functions.PreviewMention(fixture.Request(JsonSerializer.Serialize(new { question })));
        Assert.Equal(valid ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(valid ? 1 : 0, fixture.Service.Requests.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"question\":null}")]
    [InlineData("{\"question\":1}")]
    [InlineData("{\"question\":false}")]
    [InlineData("{\"question\":\"Hi\",\"fromName\":1}")]
    [InlineData("{\"question\":\"Hi\",\"fromUsername\":{}}")]
    [InlineData("{\"question\":\"Hi\",\"replyTo\":\"text\"}")]
    [InlineData("{\"question\":\"Hi\",\"replyTo\":{}}")]
    [InlineData("{\"question\":\"Hi\",\"replyTo\":{\"text\":null}}")]
    [InlineData("{\"question\":\"Hi\",\"replyTo\":{\"text\":1}}")]
    [InlineData("{\"question\":\"Hi\",\"replyTo\":{\"text\":\"\",\"name\":false}}")]
    [InlineData("{\"question\":\"Hi\",\"replyTo\":{\"text\":\"\",\"username\":[]}}")]
    public async Task PreviewRejectsMalformedOrInvalidInputWithoutGenerating(string body)
    {
        using var fixture = new Fixture();
        var response = await fixture.Functions.PreviewMention(fixture.Request(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fixture.Service.Requests);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        Assert.NotEmpty(document.RootElement.GetProperty("error").GetString()!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewReportsGenericAiFailureIncludingNonCallerCancellation(bool serviceCancellation)
    {
        using var fixture = new Fixture();
        fixture.Service.Generate = _ => throw (serviceCancellation
            ? new OperationCanceledException("Sensitive upstream details")
            : new InvalidOperationException("Sensitive upstream details"));
        var response = await fixture.Functions.PreviewMention(fixture.Request("{\"question\":\"Hi\"}"));
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(response.Body);
        Assert.Equal("Unable to generate mention preview. Please try again later.", document.RootElement.GetProperty("error").GetString());
        Assert.Single(fixture.Service.Requests);
    }

    [Fact]
    public async Task PreviewPropagatesCallerCancellationDuringGeneration()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Service.Generate = token =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<string>(token);
        };
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Functions.PreviewMention(fixture.Request("{\"question\":\"Hi\"}"), cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Single(fixture.Service.Requests);
    }

    [Fact]
    public async Task PreviewPropagatesCancellationBeforeReadingRequest()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Functions.PreviewMention(fixture.Request("{\"question\":\"Hi\"}"), cancellation.Token));
        Assert.Empty(fixture.Service.Requests);
    }

    [Fact]
    public void PreviewUsesPlatformAuthenticatedPostRoute()
    {
        var method = typeof(MentionReviewFunctions).GetMethod(nameof(MentionReviewFunctions.PreviewMention))!;
        var trigger = method.GetParameters()[0].GetCustomAttribute<HttpTriggerAttribute>()!;
        Assert.Equal(AuthorizationLevel.Anonymous, trigger.AuthLevel);
        Assert.Equal(new[] { "post" }, trigger.Methods);
        Assert.Equal("review/mention", trigger.Route);
        Assert.Equal("PreviewMention", method.GetCustomAttribute<FunctionAttribute>()!.Name);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider services = new ServiceCollection()
            .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer(new JsonSerializerOptions()))
            .BuildServiceProvider();
        public MentionStub Service { get; } = new();
        public MentionReviewFunctions Functions { get; }
        public Fixture() => Functions = new(Service, NullLogger<MentionReviewFunctions>.Instance);
        public HttpRequestData Request(string body) => new TestRequest(new TestContext { InstanceServices = services }, body);
        public void Dispose() => services.Dispose();
    }

    private sealed class MentionStub : IMentionService
    {
        public List<MentionRequest> Requests { get; } = [];
        public CancellationToken CancellationToken { get; private set; }
        public Func<CancellationToken, Task<string>> Generate { get; set; } = _ => Task.FromResult("Preview answer");
        public Task<string> GenerateAsync(MentionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            CancellationToken = cancellationToken;
            return Generate(cancellationToken);
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

    private sealed class TestRequest(FunctionContext context, string body) : HttpRequestData(context)
    {
        public override Stream Body { get; } = new MemoryStream(Encoding.UTF8.GetBytes(body));
        public override HttpHeadersCollection Headers { get; } = new();
        public override IReadOnlyCollection<IHttpCookie> Cookies => [];
        public override Uri Url { get; } = new("https://localhost/api/review/mention");
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
