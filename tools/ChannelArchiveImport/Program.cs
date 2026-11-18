using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using TelegramAntiSpamBot.Search;

const string usage = "Usage: ChannelArchiveImport <export-directory> <channel-id> [--limit N] [--send (--queue-url URL | --azurite)]";
if (args is ["--help"] or ["-h"])
{
    Console.WriteLine(usage);
    return 0;
}

try
{
    if (args.Length < 2 || !args[1].StartsWith("-100", StringComparison.Ordinal) ||
        !long.TryParse(args[1], out var channelId) || channelId >= -100)
        throw new ArgumentException("Pass a numeric Bot API channel ID starting with -100.");

    var send = false;
    var azurite = false;
    Uri? queueUrl = null;
    var limit = int.MaxValue;
    for (var i = 2; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--send": send = true; break;
            case "--azurite": azurite = true; break;
            case "--limit" when i + 1 < args.Length && int.TryParse(args[i + 1], out limit) && limit > 0:
                i++;
                break;
            case "--queue-url" when i + 1 < args.Length && Uri.TryCreate(args[i + 1], UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps && uri.AbsolutePath == "/" + PostIndexJob.QueueName &&
                uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0:
                queueUrl = uri;
                i++;
                break;
            default: throw new ArgumentException("Invalid option or value.");
        }
    }

    if (send ? azurite == (queueUrl is not null) : azurite || queueUrl is not null)
        throw new ArgumentException("Use --send with exactly one destination: --queue-url or --azurite.");

    var ids = new HashSet<long>();
    var payloads = new List<(long Id, byte[] Json)>();
    var empty = 0;
    var duplicates = 0;
    var oversized = 0;
    foreach (var (id, job) in TelegramHtmlArchive.Read(args[0], channelId))
    {
        if (!ids.Add(id)) { duplicates++; continue; }
        if (job is null) { empty++; continue; }

        var json = JsonSerializer.SerializeToUtf8Bytes(job);
        if (Base64Size(json.Length) > 64 * 1024)
        {
            Console.Error.WriteLine($"Skipped post {id}: queue payload exceeds 64 KiB after Base64 encoding.");
            oversized++;
            continue;
        }

        payloads.Add((id, json));
        if (payloads.Count == limit) break;
    }

    Console.WriteLine($"Channel {channelId}: ready={payloads.Count}, no text={empty}, duplicates={duplicates}, oversized={oversized}.");
    if (payloads.Count == 0) return 1;
    if (!send)
    {
        Console.WriteLine("Dry run: nothing sent.");
        return oversized == 0 ? 0 : 1;
    }

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
    var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
    var queue = azurite
        ? new QueueClient("UseDevelopmentStorage=true", PostIndexJob.QueueName, options)
        : new QueueClient(queueUrl!, new AzureCliCredential(), options);
    var sent = 0;
    try
    {
        foreach (var (id, json) in payloads)
        {
            try
            {
                await queue.SendMessageAsync(new BinaryData(json), timeToLive: TimeSpan.FromDays(1), cancellationToken: cancellation.Token);
                sent++;
            }
            catch (Exception error) when (error is RequestFailedException or AuthenticationFailedException)
            {
                Console.Error.WriteLine($"Send failed for post {id}: {error.Message}");
                return 1;
            }
        }
    }
    finally
    {
        Console.WriteLine($"Sent {sent}/{payloads.Count} to {PostIndexJob.QueueName}.");
    }
    return oversized == 0 ? 0 : 1;
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(error.Message);
    Console.Error.WriteLine(usage);
    return 1;
}

static long Base64Size(int bytes) => ((bytes + 2L) / 3) * 4;
