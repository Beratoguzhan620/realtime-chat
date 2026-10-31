namespace RealtimeChat.Core.Conversations;

/// <summary>A person in a conversation, and how far they have read.</summary>
public sealed class Member
{
    private Member()
    {
    }

    public Guid ConversationId { get; private set; }

    public Guid UserId { get; private set; }

    public MemberRole Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    /// <summary>The sequence number of the last message this member has read; 0 before reading anything.</summary>
    public long LastReadSeq { get; private set; }

    /// <summary>
    /// The last message an "unread messages" email was sent for. A conversation that goes quiet with unread
    /// messages for an offline member sends one email, not one per message.
    /// </summary>
    public long NotifiedSeq { get; private set; }

    internal static Member Create(Guid conversationId, Guid userId, MemberRole role, long lastReadSeq, DateTimeOffset now) =>
        new() { ConversationId = conversationId, UserId = userId, Role = role, LastReadSeq = lastReadSeq, NotifiedSeq = lastReadSeq, JoinedAt = now };

    internal void Promote() => Role = MemberRole.Owner;

    /// <summary>Moves the read marker forward only; reading an older message again changes nothing.</summary>
    public bool MarkRead(long seq, long lastSeq)
    {
        var target = Math.Min(seq, lastSeq);
        if (target <= LastReadSeq)
        {
            return false;
        }

        LastReadSeq = target;
        return true;
    }
}
