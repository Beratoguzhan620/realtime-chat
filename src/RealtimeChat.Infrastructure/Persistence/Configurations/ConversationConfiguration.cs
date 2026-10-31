using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RealtimeChat.Core.Conversations;
using RealtimeChat.Core.Users;

namespace RealtimeChat.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations", table => table.HasCheckConstraint(
            "ck_conversations_direct_key", "(kind = 'Direct') = (direct_key IS NOT NULL)"));
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Title).HasMaxLength(Conversation.TitleMaxLength);
        builder.Property(c => c.DirectKey).HasMaxLength(65);
        builder.HasIndex(c => c.DirectKey).IsUnique();
        builder.HasMany(c => c.Members).WithOne().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("members");
        builder.HasKey(m => new { m.ConversationId, m.UserId });
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(10);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        // "My conversations" starts from the user.
        builder.HasIndex(m => m.UserId);
    }
}
