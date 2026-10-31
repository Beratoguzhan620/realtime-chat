using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RealtimeChat.Infrastructure.Persistence;

/// <summary>
/// The two statements that must be atomic in the database rather than in memory. Taking the next number is one
/// <c>UPDATE … RETURNING</c>: it locks the conversation row until the transaction ends, so two senders queue
/// for it and never get the same number, and a rolled-back send gives its number back.
/// </summary>
public static class MessageSequence
{
    /// <summary>
    /// Takes the next number and marks it read for the sender, in one round trip: the sender has read their own
    /// message.
    /// </summary>
    public static async Task<long> NextAsync(ChatDbContext db, Guid conversationId, Guid senderId, DateTimeOffset now, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = Command(db, """
            WITH next AS (
                UPDATE conversations SET last_seq = last_seq + 1, last_activity_at = @now
                WHERE id = @id
                RETURNING last_seq
            ), sender AS (
                UPDATE members SET last_read_seq = (SELECT last_seq FROM next)
                WHERE conversation_id = @id AND user_id = @sender
            )
            SELECT last_seq FROM next
            """);
        Add(command, "id", conversationId);
        Add(command, "sender", senderId);
        Add(command, "now", now);
        return (long)(await command.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("The conversation disappeared while sending."));
    }

    /// <summary>Moves a member's read marker forward, never back and never past the last message. Returns the new marker, or null when nothing changed.</summary>
    public static async Task<long?> MarkReadAsync(ChatDbContext db, Guid conversationId, Guid userId, long seq, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        await using var command = Command(db, """
            UPDATE members m SET last_read_seq = LEAST(@seq, c.last_seq)
            FROM conversations c
            WHERE c.id = m.conversation_id AND m.conversation_id = @conversation AND m.user_id = @user
              AND m.last_read_seq < LEAST(@seq, c.last_seq)
            RETURNING m.last_read_seq
            """);
        Add(command, "conversation", conversationId);
        Add(command, "user", userId);
        Add(command, "seq", seq);
        return await command.ExecuteScalarAsync(ct) is long marker ? marker : null;
    }

    private static DbCommand Command(ChatDbContext db, string sql)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
