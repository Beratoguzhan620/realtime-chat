using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RealtimeChat.Api.Common;
using RealtimeChat.Core;
using RealtimeChat.Core.Conversations;
using RealtimeChat.Infrastructure.Persistence;

namespace RealtimeChat.Api.Features.Conversations;

public sealed record StartDirectRequest(Guid UserId);

public sealed record StartGroupRequest(string Title, IReadOnlyList<Guid> MemberIds);

public sealed record AddMembersRequest(IReadOnlyList<Guid> UserIds);

public sealed record RenameRequest(string Title);

internal static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/conversations").WithTags("Conversations").RequireAuthorization();

        group.MapGet("/", List).WithSummary("My conversations, most recently active first, with unread counts");
        group.MapGet("/{id:guid}", Get).WithSummary("One conversation");
        group.MapPost("/direct", StartDirect)
            .WithSummary("Start or reopen a conversation with one person")
            .WithDescription("A pair has one direct conversation: asking again returns the existing one with 200.");
        group.MapPost("/groups", StartGroup).WithSummary("Start a group");
        group.MapPost("/{id:guid}/members", AddMembers).WithSummary("Add people to a group (owner)");
        group.MapPatch("/{id:guid}", Rename).WithSummary("Rename a group (owner)");
        group.MapPost("/{id:guid}/leave", Leave).WithSummary("Leave a group");
        return app;
    }

    private static async Task<Ok<List<ConversationResponse>>> List(ClaimsPrincipal principal, ChatDbContext db, CancellationToken ct)
    {
        var me = principal.UserId();
        var conversations = await db.Conversations.AsNoTracking()
            .Include(c => c.Members)
            .Where(c => c.Members.Any(m => m.UserId == me))
            .OrderByDescending(c => c.LastActivityAt)
            .Take(200)
            .ToListAsync(ct);
        return TypedResults.Ok(await ConversationResponses.BuildAsync(db, conversations, me, ct));
    }

    private static async Task<Ok<ConversationResponse>> Get(Guid id, ClaimsPrincipal principal, ChatDbContext db, CancellationToken ct)
    {
        var me = principal.UserId();
        var conversation = await ConversationResponses.LoadForMemberAsync(db, id, me, ct);
        return TypedResults.Ok(await ConversationResponses.BuildAsync(db, conversation, me, ct));
    }

    private static async Task<Results<Created<ConversationResponse>, Ok<ConversationResponse>>> StartDirect(
        StartDirectRequest request, ClaimsPrincipal principal, ChatDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var me = principal.UserId();
        await RequireUsersAsync(db, [request.UserId], ct);

        var key = Conversation.DirectKeyFor(me, request.UserId);
        var existing = await db.Conversations.Include(c => c.Members).SingleOrDefaultAsync(c => c.DirectKey == key, ct);
        if (existing is not null)
        {
            return TypedResults.Ok(await ConversationResponses.BuildAsync(db, existing, me, ct));
        }

        var conversation = Conversation.StartDirect(me, request.UserId, clock.GetUtcNow());
        db.Conversations.Add(conversation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_conversations_direct_key"))
        {
            // The other person started the same conversation at the same moment; theirs wins.
            db.ChangeTracker.Clear();
            var winner = await db.Conversations.Include(c => c.Members).SingleAsync(c => c.DirectKey == key, ct);
            return TypedResults.Ok(await ConversationResponses.BuildAsync(db, winner, me, ct));
        }

        return TypedResults.Created($"/api/conversations/{conversation.Id}", await ConversationResponses.BuildAsync(db, conversation, me, ct));
    }

    private static async Task<Results<Created<ConversationResponse>, ValidationProblem>> StartGroup(
        StartGroupRequest request, ClaimsPrincipal principal, ChatDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Title), "title", "Give the group a title.")
            .Require(request.MemberIds is { Count: > 0 }, "memberIds", "Add at least one other person.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var me = principal.UserId();
        var others = request.MemberIds.Where(id => id != me).Distinct().ToList();
        await RequireUsersAsync(db, others, ct);

        var conversation = Conversation.StartGroup(me, request.Title, others, clock.GetUtcNow());
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/conversations/{conversation.Id}", await ConversationResponses.BuildAsync(db, conversation, me, ct));
    }

    private static async Task<Results<Ok<ConversationResponse>, ValidationProblem>> AddMembers(
        Guid id, AddMembersRequest request, ClaimsPrincipal principal, ChatDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation().Require(request.UserIds is { Count: > 0 }, "userIds", "Name at least one person.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var me = principal.UserId();
        var conversation = await ConversationResponses.LoadForMemberAsync(db, id, me, ct);
        await RequireUsersAsync(db, request.UserIds, ct);
        conversation.AddMembers(me, request.UserIds, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ConversationResponses.BuildAsync(db, conversation, me, ct));
    }

    private static async Task<Ok<ConversationResponse>> Rename(
        Guid id, RenameRequest request, ClaimsPrincipal principal, ChatDbContext db, CancellationToken ct)
    {
        var me = principal.UserId();
        var conversation = await ConversationResponses.LoadForMemberAsync(db, id, me, ct);
        conversation.Rename(me, request.Title);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ConversationResponses.BuildAsync(db, conversation, me, ct));
    }

    private static async Task<NoContent> Leave(Guid id, ClaimsPrincipal principal, ChatDbContext db, CancellationToken ct)
    {
        var me = principal.UserId();
        var conversation = await ConversationResponses.LoadForMemberAsync(db, id, me, ct);
        conversation.Leave(me);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task RequireUsersAsync(ChatDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        var found = await db.Users.CountAsync(u => distinct.Contains(u.Id), ct);
        if (found != distinct.Count)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Some of these people do not exist.");
        }
    }
}
