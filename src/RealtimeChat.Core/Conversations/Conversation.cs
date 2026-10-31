namespace RealtimeChat.Core.Conversations;

/// <summary>
/// A direct or group conversation. Every message gets the next number in <see cref="LastSeq"/>, so clients can
/// tell exactly which messages they missed and ask for everything after the last one they have.
/// </summary>
public sealed class Conversation
{
    public const int MaxGroupMembers = 100;
    public const int TitleMaxLength = 80;

    private readonly List<Member> _members = [];

    private Conversation()
    {
    }

    public Guid Id { get; private set; }

    public ConversationKind Kind { get; private set; }

    /// <summary>Only groups have a title; a direct conversation is named after the other person.</summary>
    public string? Title { get; private set; }

    /// <summary>For direct conversations, both user ids in order; unique, so a pair has one conversation.</summary>
    public string? DirectKey { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// The sequence number of the newest message; 0 while the conversation is empty. It is advanced in the
    /// database with one atomic <c>UPDATE … RETURNING</c>, never in memory, so two senders cannot get the same number.
    /// </summary>
    public long LastSeq { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public IReadOnlyList<Member> Members => _members;

    public static string DirectKeyFor(Guid a, Guid b) => a.CompareTo(b) < 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";

    public static Conversation StartDirect(Guid starter, Guid other, DateTimeOffset now)
    {
        if (starter == other)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A direct conversation needs another person.");
        }

        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(now),
            Kind = ConversationKind.Direct,
            DirectKey = DirectKeyFor(starter, other),
            CreatedBy = starter,
            CreatedAt = now,
            LastActivityAt = now,
        };
        conversation._members.Add(Member.Create(conversation.Id, starter, MemberRole.Member, 0, now));
        conversation._members.Add(Member.Create(conversation.Id, other, MemberRole.Member, 0, now));
        return conversation;
    }

    public static Conversation StartGroup(Guid owner, string title, IEnumerable<Guid> memberIds, DateTimeOffset now)
    {
        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(now),
            Kind = ConversationKind.Group,
            Title = CheckTitle(title),
            CreatedBy = owner,
            CreatedAt = now,
            LastActivityAt = now,
        };
        conversation._members.Add(Member.Create(conversation.Id, owner, MemberRole.Owner, 0, now));
        conversation.AddMembersCore(memberIds, now);
        return conversation;
    }

    public bool IsMember(Guid userId) => _members.Exists(m => m.UserId == userId);

    public Member MemberOf(Guid userId) =>
        _members.Find(m => m.UserId == userId)
        ?? throw new DomainException(ErrorCodes.NotFound, "There is no conversation with this id.");

    /// <summary>Adds people to a group. Anyone already in it is skipped. Returns the new members.</summary>
    public IReadOnlyList<Member> AddMembers(Guid by, IEnumerable<Guid> userIds, DateTimeOffset now)
    {
        RequireGroup();
        if (MemberOf(by).Role != MemberRole.Owner)
        {
            throw new DomainException(ErrorCodes.Forbidden, "Only the owner adds people to a group.");
        }

        return AddMembersCore(userIds, now);
    }

    public void Rename(Guid by, string title)
    {
        RequireGroup();
        if (MemberOf(by).Role != MemberRole.Owner)
        {
            throw new DomainException(ErrorCodes.Forbidden, "Only the owner renames a group.");
        }

        Title = CheckTitle(title);
    }

    /// <summary>Leaves a group. When the owner leaves, the longest-standing member becomes the owner.</summary>
    public void Leave(Guid userId)
    {
        RequireGroup();
        var member = MemberOf(userId);
        _members.Remove(member);
        if (member.Role == MemberRole.Owner && _members.Count > 0 && !_members.Exists(m => m.Role == MemberRole.Owner))
        {
            _members.OrderBy(m => m.JoinedAt).ThenBy(m => m.UserId).First().Promote();
        }
    }

    private IReadOnlyList<Member> AddMembersCore(IEnumerable<Guid> userIds, DateTimeOffset now)
    {
        var added = new List<Member>();
        foreach (var userId in userIds.Distinct())
        {
            if (IsMember(userId))
            {
                continue;
            }

            if (_members.Count >= MaxGroupMembers)
            {
                throw new DomainException(ErrorCodes.ValidationFailed, $"A group has at most {MaxGroupMembers} members.");
            }

            // New members start as having read everything so far: history is visible, but not unread.
            var member = Member.Create(Id, userId, MemberRole.Member, LastSeq, now);
            _members.Add(member);
            added.Add(member);
        }

        return added;
    }

    private void RequireGroup()
    {
        if (Kind != ConversationKind.Group)
        {
            throw new DomainException(ErrorCodes.InvalidState, "Direct conversations always have the same two people.");
        }
    }

    private static string CheckTitle(string title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > TitleMaxLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A group title has 1 to {TitleMaxLength} characters.");
        }

        return trimmed;
    }
}
