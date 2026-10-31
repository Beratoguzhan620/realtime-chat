namespace RealtimeChat.Core.Conversations;

public enum MemberRole
{
    Member,

    /// <summary>Started the group (or inherited it); may add people and rename it.</summary>
    Owner,
}
