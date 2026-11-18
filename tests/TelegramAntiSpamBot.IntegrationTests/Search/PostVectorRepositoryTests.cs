namespace TelegramAntiSpamBot.IntegrationTests;

public sealed class PostVectorRepositoryTests
{
    [Fact]
    public async Task ReplacementRemovesOldChunksInTheSameTransaction()
    {
        var table = new VectorTableStub();
        await using var services = new ServiceCollection().AddPersistence().AddKeyedSingleton<TableClient>("Vectors", table).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPostVectorRepository>();
        var vector = new float[3072];
        vector[0] = 1;

        await repository.SaveAsync(-100, new IndexedPost(42, "Title", [vector]));

        Assert.Equal("PartitionKey eq '-100' and RowKey ge '42:' and RowKey lt '42;'", table.Filter);
        Assert.Collection(table.Actions,
            action => AssertAction(action, TableTransactionActionType.UpdateReplace, "42:chunk:0000"),
            action => AssertAction(action, TableTransactionActionType.Delete, "42:chunk:0001"),
            action => AssertAction(action, TableTransactionActionType.UpdateReplace, "42:post"));
        Assert.Equal(new byte[] { 0, 0, 128, 63 }, ((TableEntity)table.Actions[0].Entity).GetBinary("Vector")![..4]);
    }

    [Fact]
    public async Task SaveDoesNotOverwriteNewerRevision()
    {
        var table = new VectorTableStub { Revision = 20 };
        await using var services = new ServiceCollection().AddPersistence().AddKeyedSingleton<TableClient>("Vectors", table).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPostVectorRepository>();
        var vector = new float[3072];
        vector[0] = 1;

        await repository.SaveAsync(-100, new IndexedPost(42, "Earlier title", [vector]) { Revision = 19 });

        Assert.Empty(table.Actions);
    }

    private static void AssertAction(TableTransactionAction action, TableTransactionActionType type, string key)
    {
        Assert.Equal(type, action.ActionType);
        Assert.Equal(key, action.Entity.RowKey);
        Assert.Equal(new ETag("existing"), action.ETag);
    }
}
