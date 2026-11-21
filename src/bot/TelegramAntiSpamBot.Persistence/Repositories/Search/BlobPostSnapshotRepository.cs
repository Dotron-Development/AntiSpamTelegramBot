namespace TelegramAntiSpamBot.Persistence.Repositories;

internal sealed class BlobPostSnapshotRepository(BlobContainerClient container) : IPostSnapshotRepository
{
    // Rebuilds use temporary disk space; limit work per Functions worker.
    private readonly SemaphoreSlim builds = new(1);

    public async IAsyncEnumerable<SnapshotBlob> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
            if (blob.Properties.ContentLength is long size && blob.Properties.LastModified is DateTimeOffset modified)
                yield return new SnapshotBlob(blob.Name, size, modified);
    }

    public async Task<SnapshotBlob?> FindAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var properties = (await container.GetBlobClient(name).GetPropertiesAsync(cancellationToken: cancellationToken)).Value;
            return new SnapshotBlob(name, properties.ContentLength, properties.LastModified);
        }
        catch (RequestFailedException e) when (e.Status == 404 && e.ErrorCode == "BlobNotFound")
        {
            return null;
        }
    }

    public async Task<SnapshotDownload?> OpenIfChangedAsync(string name, string? etag, CancellationToken cancellationToken)
    {
        var conditions = etag is null ? null : new BlobRequestConditions { IfNoneMatch = new ETag(etag) };
        try
        {
            var response = await container.GetBlobClient(name).DownloadStreamingAsync(
                new BlobDownloadOptions { Conditions = conditions }, cancellationToken);
            if (response.GetRawResponse().Status == 304) return new SnapshotDownload(etag!, 0, null);
            var download = response.Value;
            return new SnapshotDownload(download.Details.ETag.ToString(), download.Details.ContentLength, download.Content);
        }
        catch (RequestFailedException e) when (e.Status == 304)
        {
            return new SnapshotDownload(etag!, 0, null);
        }
        catch (RequestFailedException e) when (e.Status == 404 && e.ErrorCode == "BlobNotFound")
        {
            return null;
        }
    }

    public async Task PublishAsync(string name, DateTimeOffset requestedAt, Func<Stream, CancellationToken, Task<bool>> write,
        CancellationToken cancellationToken)
    {
        await builds.WaitAsync(cancellationToken);
        try
        {
            await BuildAndPublishAsync(name, requestedAt, write, cancellationToken);
        }
        finally
        {
            builds.Release();
        }
    }

    private async Task BuildAndPublishAsync(string name, DateTimeOffset requestedAt,
        Func<Stream, CancellationToken, Task<bool>> write, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var blob = container.GetBlobClient(name);
        var conditions = new BlobRequestConditions();
        try
        {
            var current = (await blob.GetPropertiesAsync(cancellationToken: cancellationToken)).Value;
            if (current.Metadata.TryGetValue("buildstarted", out var watermark) &&
                long.TryParse(watermark, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks >= requestedAt.UtcTicks)
                return;
            conditions.IfMatch = current.ETag;
        }
        catch (RequestFailedException e) when (e.Status == 404 && e.ErrorCode == "BlobNotFound")
        {
            conditions.IfNoneMatch = ETag.All;
        }

        await using var file = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        if (!await write(file, cancellationToken)) return;
        file.Position = 0;
        await blob.UploadAsync(file, new BlobUploadOptions
        {
            Conditions = conditions,
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/octet-stream" },
            Metadata = new Dictionary<string, string> { ["buildstarted"] = started.UtcTicks.ToString(CultureInfo.InvariantCulture) },
            TransferOptions = new StorageTransferOptions { MaximumConcurrency = 1, MaximumTransferSize = 8 * 1024 * 1024 }
        }, cancellationToken);
    }
}
