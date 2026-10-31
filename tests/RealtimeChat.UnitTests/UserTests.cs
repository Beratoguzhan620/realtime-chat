using RealtimeChat.Core;
using RealtimeChat.Core.Users;

namespace RealtimeChat.UnitTests;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Emails_are_normalized_and_names_trimmed()
    {
        var user = User.Register("  Ayse.Yilmaz@Example.COM ", "  Ayşe Yılmaz ", Now);

        Assert.Equal("ayse.yilmaz@example.com", user.Email);
        Assert.Equal("Ayşe Yılmaz", user.DisplayName);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("   ")]
    public void Display_names_need_two_characters(string name) =>
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => User.Register("a@example.com", name, Now)).Code);

    [Fact]
    public void Display_names_are_at_most_fifty_characters() =>
        Assert.Throws<DomainException>(() => User.Register("a@example.com", new string('a', 51), Now));
}
