using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RealtimeChat.Core.Conversations;
using RealtimeChat.Core.Messages;
using RealtimeChat.Core.Users;

namespace RealtimeChat.Infrastructure.Persistence.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Body).HasMaxLength(Message.BodyMaxLength);
        builder.HasOne<Conversation>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Restrict);

        // One place per number: history pages and catch-up read this index in order.
        builder.HasIndex(m => new { m.ConversationId, m.Seq }).IsUnique();

        // A resend from the same device is the same message.
        builder.HasIndex(m => new { m.ConversationId, m.SenderId, m.ClientMessageId }).IsUnique();
        builder.Ignore(m => m.IsDeleted);
    }
}
