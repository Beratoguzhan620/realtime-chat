using Microsoft.EntityFrameworkCore;
using RealtimeChat.Api.Features.Hub;
using RealtimeChat.Core;
using RealtimeChat.Core.Messages;
using RealtimeChat.Infrastructure.Persistence;

namespace RealtimeChat.Api.Features.Messages;

/// <summary>Everything that reads or changes messages, used by both the hub and the REST endpoints.</summary>
internal sealed partial class MessageService(
    ChatDbContext db, ChatEvents events, SendRateLimiter limiter, TimeProvider clock, ILogger<MessageService> logger)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <summary>
    /// Stores a message and tells every member. A resend with the same client message id returns the stored
    /// message and tells nobody again, so a client may retry whenever it is unsure.
    /// </summary>
    public async Task<SendResult> SendAsync(Guid senderId, Guid conversationId, Guid clientMessageId, string body, CancellationToken ct)
    {
        var existing = await FindResendAsync(senderId, conversationId, clientMessageId, ct);
        if (existing is not null)
        {
            return new SendResult(MessageResponse.From(existing), Replayed: true);
        }

        var checkedBody = Message.CheckBody(body);
        limiter.Take(senderId);
        var members = await MemberIdsAsync(conversationId, senderId, ct);

        Message message;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var now = clock.GetUtcNow();
            var seq = await MessageSequence.NextAsync(db, conversationId, now, ct);
            message = Message.Create(conversationId, seq, senderId, clientMessageId, checkedBody, now);
            db.Messages.Add(message);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_messages_conversation_id_sender_id_client_message_id"))
            {
                // The same message arrived twice at the same moment (two tabs, a retry racing the original).
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                var winner = await FindResendAsync(senderId, conversationId, clientMessageId, ct)
                    ?? throw new InvalidOperationException("A duplicate message vanished.");
                return new SendResult(MessageResponse.From(winner), Replayed: true);
            }

            // Senders have read their own message.
            await MessageSequence.MarkReadAsync(db, conversationId, senderId, seq, ct);
            await transaction.CommitAsync(ct);
        }

        LogSent(logger, conversationId, message.Seq, senderId);
        var response = MessageResponse.From(message);
        await events.MessageReceived(members, response);
        return new SendResult(response, Replayed: false);
    }

    public async Task<MessageResponse> EditAsync(Guid userId, Guid messageId, string body, CancellationToken ct)
    {
        var message = await LoadForMemberAsync(userId, messageId, ct);
        message.Edit(userId, body, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        var response = MessageResponse.From(message);
        await events.MessageUpdated(await MemberIdsAsync(message.ConversationId, userId, ct), response);
        return response;
    }

    public async Task<MessageResponse> DeleteAsync(Guid userId, Guid messageId, CancellationToken ct)
    {
        var message = await LoadForMemberAsync(userId, messageId, ct);
        message.Delete(userId, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        var response = MessageResponse.From(message);
        await events.MessageUpdated(await MemberIdsAsync(message.ConversationId, userId, ct), response);
        return response;
    }

    /// <summary>
    /// History in ascending order. <paramref name="after"/> catches up after a reconnect (oldest first, from just
    /// after that number); <paramref name="before"/> pages back into history; neither gives the newest page.
    /// </summary>
    public async Task<MessagePage> PageAsync(Guid userId, Guid conversationId, long? before, long? after, int limit, CancellationToken ct)
    {
        if (before is not null && after is not null)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Ask for messages before or after a number, not both.");
        }

        if (limit is < 1 or > MaxPageSize)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"The limit is 1 to {MaxPageSize}.");
        }

        await MemberIdsAsync(conversationId, userId, ct);
        var messages = db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        List<Message> page;
        if (after is { } from)
        {
            page = await messages.Where(m => m.Seq > from).OrderBy(m => m.Seq).Take(limit + 1).ToListAsync(ct);
        }
        else
        {
            var query = before is { } until ? messages.Where(m => m.Seq < until) : messages;
            page = await query.OrderByDescending(m => m.Seq).Take(limit + 1).ToListAsync(ct);
            page.Reverse();
            if (page.Count > limit)
            {
                page.RemoveAt(0);
                return new MessagePage(page.Select(MessageResponse.From).ToList(), HasMore: true);
            }

            return new MessagePage(page.Select(MessageResponse.From).ToList(), HasMore: false);
        }

        var hasMore = page.Count > limit;
        return new MessagePage(page.Take(limit).Select(MessageResponse.From).ToList(), hasMore);
    }

    /// <summary>Moves the reader's marker forward and tells the other members, so their messages show as read.</summary>
    public async Task<long?> MarkReadAsync(Guid userId, Guid conversationId, long seq, CancellationToken ct)
    {
        if (seq < 1)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Messages are numbered from 1.");
        }

        var members = await MemberIdsAsync(conversationId, userId, ct);
        var marker = await MessageSequence.MarkReadAsync(db, conversationId, userId, seq, ct);
        if (marker is { } value)
        {
            await events.ReadUpdated(members, new ReadReceipt(conversationId, userId, value));
        }

        return marker;
    }

    /// <summary>The members of a conversation the caller belongs to. Anyone else gets "not found".</summary>
    public async Task<List<Guid>> MemberIdsAsync(Guid conversationId, Guid userId, CancellationToken ct)
    {
        var members = await db.Members.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .Select(m => m.UserId)
            .ToListAsync(ct);
        if (!members.Contains(userId))
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no conversation with this id.");
        }

        return members;
    }

    private Task<Message?> FindResendAsync(Guid senderId, Guid conversationId, Guid clientMessageId, CancellationToken ct) =>
        db.Messages.AsNoTracking().SingleOrDefaultAsync(
            m => m.ConversationId == conversationId && m.SenderId == senderId && m.ClientMessageId == clientMessageId, ct);

    private async Task<Message> LoadForMemberAsync(Guid userId, Guid messageId, CancellationToken ct)
    {
        var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == messageId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no message with this id.");
        if (!await db.Members.AnyAsync(m => m.ConversationId == message.ConversationId && m.UserId == userId, ct))
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no message with this id.");
        }

        return message;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Message {Seq} stored in conversation {ConversationId} from {SenderId}")]
    private static partial void LogSent(ILogger logger, Guid conversationId, long seq, Guid senderId);
}
