namespace TelegramAntiSpamBot.Persistence.DTO;

[JsonConverter(typeof(JsonStringEnumConverter<HistoryUpdateType>))]
public enum HistoryUpdateType
{
    Message,
    EditedMessage,
    ChannelPost,
    EditedChannelPost
}
