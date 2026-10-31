namespace RealtimeChat.Core.Conversations;

public enum ConversationKind
{
    /// <summary>Two people. There is at most one direct conversation per pair.</summary>
    Direct,

    /// <summary>A titled conversation with up to <see cref="Conversation.MaxGroupMembers"/> members.</summary>
    Group,
}
