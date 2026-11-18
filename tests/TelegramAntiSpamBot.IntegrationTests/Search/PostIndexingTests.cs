namespace TelegramAntiSpamBot.IntegrationTests;

public sealed class PostIndexingTests
{
    [Fact]
    public void QueuesOnlyAutomaticDiscussionCopiesWithTextAndKeepsMultilineCaptionTitle()
    {
        const string json = """
            {"update_id":10,"message":{"message_id":42,"date":1720000000,
              "chat":{"id":-10001,"type":"supergroup"},"is_automatic_forward":true,
              "caption":"#Async\nPart 13\n\nPost body", "caption_entities":[
                {"type":"bold","offset":0,"length":6},{"type":"bold","offset":7,"length":7}]}}
            """;
        var job = TelegramPostIndexJob.FromUpdate(JsonSerializer.Deserialize<Update>(json, JsonBotAPI.Options)!);
        Assert.NotNull(job);
        Assert.Equal(-10001, job.ChatId);
        Assert.Equal(42, job.MessageId);
        Assert.Equal("#Async\nPart 13", job.Text.Substring(job.TitleOffset, job.TitleLength));
        foreach (var ignored in new[] { json.Replace("true", "false"), json.Replace("\"message\":", "\"channel_post\":"), json.Replace("#Async\\nPart 13\\n\\nPost body", " ") })
            Assert.Null(TelegramPostIndexJob.FromUpdate(JsonSerializer.Deserialize<Update>(ignored, JsonBotAPI.Options)!));
    }

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
