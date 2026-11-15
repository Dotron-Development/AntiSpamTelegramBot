namespace TelegramAntiSpamBot.Persistence.DTO;

public sealed record HistoryMessage(long ChatId, int MessageId, long DateTimeTicks, HistoryMessageContent Content,
    HistoryUpdateType UpdateType = HistoryUpdateType.Message, int? UpdateId = null);

[method: JsonConstructor]
public sealed record HistoryMessageContent(MessageFrom? From, HistoryMessageReply? ReplyTo, MessagePayload Payload)
{
    public HistoryMessageContent(MessageFrom? from, HistoryMessageReply? replyTo, string text)
        : this(from, replyTo, new MessagePayload(UserMessage: text)) { }

    [JsonIgnore]
    public string Text => Payload.UserMessage ?? string.Empty;
}

public sealed record HistoryMessageReply(int MessageId, MessageFrom? From, string Text);