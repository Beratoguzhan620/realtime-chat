using Microsoft.EntityFrameworkCore;
using RealtimeChat.Infrastructure.Persistence;
using RealtimeChat.Infrastructure.Presence;

namespace RealtimeChat.Api.Features.Hub;

/// <summary>Tells the people someone shares a conversation with that they came online or went offline. Nobody else learns it.</summary>
internal sealed class PresenceNotifier(IServiceScopeFactory scopes, ChatEvents events) : IPresenceListener
{
    public async Task ChangedAsync(Guid userId, bool online, CancellationToken ct)
    {
        var contacts = await ContactsAsync(userId, ct);
        if (contacts.Count > 0)
        {
            await events.PresenceChanged(contacts, new PresenceEvent(userId, online));
        }
    }

    public async Task WentOfflineAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
    {
        foreach (var userId in userIds)
        {
            await ChangedAsync(userId, online: false, cancellationToken);
        }
    }

    public async Task<List<Guid>> ContactsAsync(Guid userId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        return await db.Members.AsNoTracking()
            .Where(m => m.UserId != userId && db.Members.Any(mine => mine.UserId == userId && mine.ConversationId == m.ConversationId))
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);
    }
}
