namespace TelegramAntiSpamBot.OpenAI;

internal sealed class MentionHistoryToolFactory(
    IMessageHistoryRepository historyRepository,
    ILogger<MentionHistoryTool> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(AIJsonUtilities.DefaultOptions)
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public AITool Create(MentionConversation conversation, CancellationToken cancellationToken)
    {
        var tool = new MentionHistoryTool(historyRepository, conversation, cancellationToken, logger);
        return AIFunctionFactory.Create(tool.GetMessageHistoryAsync, name: "get_message_history",
            serializerOptions: SerializerOptions);
    }
}
