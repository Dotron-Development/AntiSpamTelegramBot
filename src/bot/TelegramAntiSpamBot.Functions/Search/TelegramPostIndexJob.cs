namespace TelegramAntiSpamBot.Functions;

internal static class TelegramPostIndexJob
{
    public static PostIndexJob? FromUpdate(Update update)
    {
        var message = update.Type switch
        {
            UpdateType.Message => update.Message,
            UpdateType.EditedMessage => update.EditedMessage,
            _ => null
        };

        if (message is not { IsAutomaticForward: true, ForwardOrigin: MessageOriginChannel origin } ||
            message.Chat.Type is not (ChatType.Group or ChatType.Supergroup) ||
            origin.Chat is not { Type: ChatType.Channel, Id: not 0 } || origin.MessageId <= 0)
            return null;

        var text = message.Text ?? message.Caption;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var entities = message.Text is null ? message.CaptionEntities : message.Entities;
        var start = text.Length - text.AsSpan().TrimStart().Length;
        var end = start;
        foreach (var entity in (entities ?? []).Where(entity => entity.Type == MessageEntityType.Bold).OrderBy(entity => entity.Offset))
        {
            if (entity.Offset < end || entity.Length <= 0 || entity.Offset > text.Length - entity.Length) continue;
            if (!text.AsSpan(end, entity.Offset - end).Trim().IsEmpty) break;
            end = entity.Offset + entity.Length;
        }
        var seconds = new DateTimeOffset(message.EditDate ?? origin.Date).ToUnixTimeSeconds();
        var revision = (seconds << 32) | (uint)update.Id;
        return new PostIndexJob(origin.Chat.Id, origin.MessageId, revision, text, start, end - start);
    }
}
