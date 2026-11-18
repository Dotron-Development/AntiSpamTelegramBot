namespace TelegramAntiSpamBot.IntegrationTests;

public sealed class PostIndexingTests
{
    private const string ChannelOrigin = """
        "forward_origin":{"type":"channel","date":1720000000,"chat":{"id":-10002,"type":"channel"},"message_id":13},
        """;
    private const string Forward = """
        {"update_id":10,"message":{"message_id":42,"date":1720000100,
          "chat":{"id":-10001,"type":"supergroup"},"is_automatic_forward":true,
        """ + ChannelOrigin + """
          "caption":"#Async\nPart 13\n\nPost body", "caption_entities":[
            {"type":"bold","offset":0,"length":6},{"type":"bold","offset":7,"length":7}]}}
        """;

    [Fact]
    public void UsesOriginalChannelAndPostIdsForAutomaticForwardsAndEdits()
    {
        var job = ParseJob(Forward);
        Assert.NotNull(job);
        Assert.Equal(-10002, job.ChatId);
        Assert.Equal(13, job.MessageId);
        Assert.Equal(1720000000L, job.Revision >> 32);
        Assert.Equal("#Async\nPart 13", job.Text.Substring(job.TitleOffset, job.TitleLength));

        var edited = ParseJob(Forward.Replace("\"message\":{", "\"edited_message\":{\"edit_date\":1720000200,"));
        Assert.NotNull(edited);
        Assert.Equal((job.ChatId, job.MessageId), (edited.ChatId, edited.MessageId));
        Assert.Equal(1720000200L, edited.Revision >> 32);
    }

    [Fact]
    public void SkipsMessagesWithoutAnAutomaticChannelOriginOrText()
    {
        Assert.Null(ParseJob(Forward.Replace(ChannelOrigin, "")));
        Assert.Null(ParseJob(Forward.Replace(ChannelOrigin, """
            "forward_origin":{"type":"hidden_user","date":1720000000,"sender_user_name":"Example"},
            """)));
        Assert.Null(ParseJob(Forward.Replace("true", "false")));
        Assert.Null(ParseJob(Forward.Replace("\"message\":", "\"channel_post\":")));
        Assert.Null(ParseJob(Forward.Replace("#Async\\nPart 13\\n\\nPost body", " ")));
    }

    private static PostIndexJob? ParseJob(string json) =>
        TelegramPostIndexJob.FromUpdate(JsonSerializer.Deserialize<Update>(json, JsonBotAPI.Options)!);

    [Fact]
    public async Task IndexerBatchesLongTextWithTitleAndSkipsAlreadyIndexedJobs()
    {
        const string title = "#Async\nPart 13";
        var text = title + "\n\n" + string.Concat(Enumerable.Repeat("Асинхронность 🚀 ", 900)) + "\n\nConfigureAwait(false)";
        var embeddings = new SearchEmbeddingsStub();
        var repository = new PostVectorRepositoryStub();
        var indexer = new PostIndexer(repository, embeddings);
        var job = new PostIndexJob(-10001, 42, 10, text, 0, title.Length);
        await indexer.IndexAsync(job);
        await indexer.IndexAsync(job);
        await indexer.IndexAsync(job with { Text = " " });

        Assert.Equal(1, repository.Saves);
        Assert.NotNull(repository.Post);
        Assert.Equal(title, repository.Post.Title);
        Assert.True(embeddings.Inputs.Count > 1);
        Assert.Equal(embeddings.Inputs.Count, repository.Post.Vectors.Count);
        Assert.All(embeddings.Inputs, input => Assert.StartsWith(title, input));
        Assert.EndsWith("ConfigureAwait(false)", embeddings.Inputs[^1]);
    }
}
