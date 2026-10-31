using RealtimeChat.Core;
using RealtimeChat.Core.Conversations;

namespace RealtimeChat.UnitTests;

public sealed class ConversationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ayse = Guid.CreateVersion7();
    private static readonly Guid Mehmet = Guid.CreateVersion7();
    private static readonly Guid Zeynep = Guid.CreateVersion7();

    [Fact]
    public void A_direct_conversation_has_the_same_key_whoever_starts_it()
    {
        var first = Conversation.StartDirect(Ayse, Mehmet, Now);
        var second = Conversation.StartDirect(Mehmet, Ayse, Now);

        Assert.Equal(first.DirectKey, second.DirectKey);
        Assert.Equal(2, first.Members.Count);
        Assert.Null(first.Title);
    }

    [Fact]
    public void Nobody_starts_a_direct_conversation_with_themselves() =>
        Assert.Equal(ErrorCodes.ValidationFailed,
            Assert.Throws<DomainException>(() => Conversation.StartDirect(Ayse, Ayse, Now)).Code);

    [Fact]
    public void A_group_has_an_owner_and_no_duplicate_members()
    {
        var group = Conversation.StartGroup(Ayse, "  Kargo ekibi  ", [Mehmet, Mehmet, Zeynep, Ayse], Now);

        Assert.Equal("Kargo ekibi", group.Title);
        Assert.Equal(3, group.Members.Count);
        Assert.Equal(MemberRole.Owner, group.MemberOf(Ayse).Role);
        Assert.Equal(MemberRole.Member, group.MemberOf(Zeynep).Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_group_needs_a_title(string title) =>
        Assert.Throws<DomainException>(() => Conversation.StartGroup(Ayse, title, [Mehmet], Now));

    [Fact]
    public void A_group_has_at_most_one_hundred_members()
    {
        var people = Enumerable.Range(0, Conversation.MaxGroupMembers).Select(_ => Guid.CreateVersion7()).ToList();

        var error = Assert.Throws<DomainException>(() => Conversation.StartGroup(Ayse, "Herkes", people, Now));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
        Assert.Equal(Conversation.MaxGroupMembers, Conversation.StartGroup(Ayse, "Neredeyse herkes", people.Skip(1), Now).Members.Count);
    }

    [Fact]
    public void Only_the_owner_adds_people_and_new_members_skip_old_unread_messages()
    {
        var group = Conversation.StartGroup(Ayse, "Proje", [Mehmet], Now);

        Assert.Equal(ErrorCodes.Forbidden, Assert.Throws<DomainException>(() => group.AddMembers(Mehmet, [Zeynep], Now)).Code);

        var added = group.AddMembers(Ayse, [Zeynep, Mehmet], Now);

        Assert.Equal(Zeynep, Assert.Single(added).UserId);
        Assert.Equal(group.LastSeq, group.MemberOf(Zeynep).LastReadSeq);
    }

    [Fact]
    public void When_the_owner_leaves_the_longest_standing_member_takes_over()
    {
        var group = Conversation.StartGroup(Ayse, "Proje", [Mehmet], Now);
        group.AddMembers(Ayse, [Zeynep], Now.AddMinutes(5));

        group.Leave(Ayse);

        Assert.False(group.IsMember(Ayse));
        Assert.Equal(MemberRole.Owner, group.MemberOf(Mehmet).Role);
        Assert.Equal(MemberRole.Member, group.MemberOf(Zeynep).Role);
    }

    [Fact]
    public void Direct_conversations_cannot_change_their_people()
    {
        var direct = Conversation.StartDirect(Ayse, Mehmet, Now);

        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => direct.Leave(Ayse)).Code);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => direct.AddMembers(Ayse, [Zeynep], Now)).Code);
        Assert.Throws<DomainException>(() => direct.Rename(Ayse, "Yeni"));
    }

    [Fact]
    public void Outsiders_are_not_found()
    {
        var direct = Conversation.StartDirect(Ayse, Mehmet, Now);

        Assert.Equal(ErrorCodes.NotFound, Assert.Throws<DomainException>(() => direct.MemberOf(Zeynep)).Code);
    }

    [Fact]
    public void The_read_marker_only_moves_forward_and_never_past_the_last_message()
    {
        var member = Conversation.StartDirect(Ayse, Mehmet, Now).MemberOf(Ayse);

        Assert.True(member.MarkRead(5, lastSeq: 10));
        Assert.False(member.MarkRead(3, lastSeq: 10));
        Assert.Equal(5, member.LastReadSeq);
        Assert.True(member.MarkRead(99, lastSeq: 10));
        Assert.Equal(10, member.LastReadSeq);
    }
}
