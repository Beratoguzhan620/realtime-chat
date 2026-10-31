using Microsoft.EntityFrameworkCore;
using RealtimeChat.Core;
using RealtimeChat.Core.Conversations;
using RealtimeChat.Infrastructure.Persistence;

namespace RealtimeChat.Api.Features.Conversations;

public sealed record MemberResponse(Guid UserId, string DisplayName, MemberRole Role, long LastReadSeq);

/// <summary>A conversation as one member sees it: direct ones are named after the other person.</summary>
public sealed record ConversationResponse(
    Guid Id,
    ConversationKind Kind,
    string Title,
    IReadOnlyList<MemberResponse> Members,
    long LastSeq,
    long UnreadCount,
    DateTimeOffset LastActivityAt);

internal static class ConversationResponses
{
    public static async Task<List<ConversationResponse>> BuildAsync(
        ChatDbContext db, IReadOnlyCollection<Conversation> conversations, Guid viewer, CancellationToken ct)
    {
        var userIds = conversations.SelectMany(c => c.Members).Select(m => m.UserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return conversations.Select(c =>
        {
            var me = c.MemberOf(viewer);
            var members = c.Members
                .OrderByDescending(m => m.Role)
                .ThenBy(m => names.GetValueOrDefault(m.UserId), StringComparer.CurrentCulture)
                .Select(m => new MemberResponse(m.UserId, names.GetValueOrDefault(m.UserId) ?? "Former user", m.Role, m.LastReadSeq))
                .ToList();
            var title = c.Kind == ConversationKind.Group
                ? c.Title!
                : members.FirstOrDefault(m => m.UserId != viewer)?.DisplayName ?? "Former user";
            return new ConversationResponse(c.Id, c.Kind, title, members, c.LastSeq, Math.Max(0, c.LastSeq - me.LastReadSeq), c.LastActivityAt);
        }).ToList();
    }

    public static async Task<ConversationResponse> BuildAsync(ChatDbContext db, Conversation conversation, Guid viewer, CancellationToken ct) =>
        (await BuildAsync(db, [conversation], viewer, ct))[0];

    /// <summary>Loads a conversation the user belongs to. Someone else's conversation is "not found", never "forbidden".</summary>
    public static async Task<Conversation> LoadForMemberAsync(ChatDbContext db, Guid id, Guid userId, CancellationToken ct)
    {
        var conversation = await db.Conversations.Include(c => c.Members).SingleOrDefaultAsync(c => c.Id == id, ct);
        if (conversation is null || !conversation.IsMember(userId))
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no conversation with this id.");
        }

        return conversation;
    }
}
