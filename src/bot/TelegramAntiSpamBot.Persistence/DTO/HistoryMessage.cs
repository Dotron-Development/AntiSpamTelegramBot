namespace TelegramAntiSpamBot.Persistence.DTO;

public sealed record HistoryMessage(long ChatId, int MessageId, long DateTimeTicks, HistoryMessageContent Content,
    HistoryUpdateType UpdateType = HistoryUpdateType.Message, int? UpdateId = null);

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record HistoryMessageContent(HistoryMessageAuthor? From, HistoryMessageReply? ReplyTo, MessagePayload Payload)
{
    public HistoryMessageContent(HistoryMessageAuthor? from, HistoryMessageReply? replyTo, string text)
        : this(from, replyTo, new MessagePayload(UserMessage: text)) { }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Text => Payload.UserMessage ?? string.Empty;
}

public sealed record HistoryMessageAuthor(long Id, string Name, string? Username);

public sealed record HistoryMessageReply(int MessageId, HistoryMessageAuthor? From, string Text);