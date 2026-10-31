namespace RealtimeChat.Core.Messages;

/// <summary>
/// One message. <see cref="Seq"/> is its place in the conversation (1, 2, 3, … without gaps);
/// <see cref="ClientMessageId"/> is chosen by the sender's device so a resend is recognised.
/// </summary>
public sealed class Message
{
    public const int BodyMaxLength = 4000;

    private Message()
    {
    }

    public Guid Id { get; private set; }

    public Guid ConversationId { get; private set; }

    public long Seq { get; private set; }

    public Guid SenderId { get; private set; }

    public Guid ClientMessageId { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? EditedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public static Message Create(Guid conversationId, long seq, Guid senderId, Guid clientMessageId, string body, DateTimeOffset now)
    {
        if (clientMessageId == Guid.Empty)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A message needs a client message id.");
        }

        return new Message
        {
            Id = Guid.CreateVersion7(now),
            ConversationId = conversationId,
            Seq = seq,
            SenderId = senderId,
            ClientMessageId = clientMessageId,
            Body = CheckBody(body),
            CreatedAt = now,
        };
    }

    public void Edit(Guid by, string body, DateTimeOffset now)
    {
        RequireSender(by);
        var checkedBody = CheckBody(body);
        if (checkedBody == Body)
        {
            return;
        }

        Body = checkedBody;
        EditedAt = now;
    }

    /// <summary>Deleting keeps the message's place in the conversation but removes what it said.</summary>
    public void Delete(Guid by, DateTimeOffset now)
    {
        RequireSender(by);
        Body = string.Empty;
        DeletedAt = now;
    }

    /// <summary>Trims the ends, keeps line breaks inside, and refuses empty or oversized text.</summary>
    public static string CheckBody(string? body)
    {
        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A message cannot be empty.");
        }

        if (trimmed.Length > BodyMaxLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A message has at most {BodyMaxLength} characters.");
        }

        return trimmed;
    }

    private void RequireSender(Guid by)
    {
        if (by != SenderId)
        {
            throw new DomainException(ErrorCodes.Forbidden, "Only the sender changes a message.");
        }

        if (IsDeleted)
        {
            throw new DomainException(ErrorCodes.InvalidState, "The message was deleted.");
        }
    }
}
