using Mizrachi.Domain;
using Mizrachi.Tests.Unit.Fakes;

namespace Mizrachi.Tests.Unit.Domain;

public sealed class PasswordPolicyTests
{
    private const string UserName = "alice.smithxyz";

    private static PasswordPolicy PolicyDenying(params string[] denied) =>
        new(new StubPasswordDenyList(denied));

    // FR-5.1 — minimum length 12

    [Fact]
    public void Rejects_one_character_under_the_minimum()
    {
        var result = PolicyDenying().Validate(new string('a', PasswordPolicy.MinLength - 1), UserName);

        Assert.False(result.IsValid);
        Assert.Equal(PasswordPolicy.Rules.TooShort, result.FailedRule);
    }

    [Fact]
    public void Accepts_exactly_the_minimum()
    {
        var result = PolicyDenying().Validate(new string('a', PasswordPolicy.MinLength), UserName);

        Assert.True(result.IsValid);
    }

    // FR-5.2 — maximum length 128, a bound on server work

    [Fact]
    public void Accepts_exactly_the_maximum()
    {
        var result = PolicyDenying().Validate(new string('a', PasswordPolicy.MaxLength), UserName);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rejects_one_character_over_the_maximum()
    {
        var result = PolicyDenying().Validate(new string('a', PasswordPolicy.MaxLength + 1), UserName);

        Assert.False(result.IsValid);
        Assert.Equal(PasswordPolicy.Rules.TooLong, result.FailedRule);
    }

    // FR-5.3 — all characters permitted, including spaces and non-ASCII

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("    leading and trailing    ")]
    [InlineData("pässwörd-über-sicher")]
    [InlineData("סיסמה-ארוכה-מאוד")]
    [InlineData("passphrase\twith\ttabs")]
    [InlineData("!@#$%^&*()_+{}|:<>?")]
    public void Accepts_any_character_including_spaces_and_non_ascii(string password)
    {
        var result = PolicyDenying().Validate(password, UserName);

        Assert.True(result.IsValid);
    }

    // FR-5.4 — no composition rules

    [Theory]
    [InlineData("abcdefghijkl")]            // lowercase only, no digit, no symbol
    [InlineData("111111111112")]            // digits only
    [InlineData("ABCDEFGHIJKL")]            // uppercase only
    public void Imposes_no_composition_rules(string password)
    {
        var result = PolicyDenying().Validate(password, UserName);

        Assert.True(result.IsValid);
    }

    // FR-5.5 — deny list

    [Fact]
    public void Rejects_a_commonly_used_password()
    {
        var result = PolicyDenying("qwertyuiop12").Validate("qwertyuiop12", UserName);

        Assert.False(result.IsValid);
        Assert.Equal(PasswordPolicy.Rules.CommonlyUsed, result.FailedRule);
    }

    [Fact]
    public void Accepts_a_password_that_is_not_on_the_deny_list()
    {
        var result = PolicyDenying("qwertyuiop12").Validate("a-different-one", UserName);

        Assert.True(result.IsValid);
    }

    // FR-5.6 — password equal to the username

    [Fact]
    public void Rejects_a_password_equal_to_the_username()
    {
        var result = PolicyDenying().Validate(UserName, UserName);

        Assert.False(result.IsValid);
        Assert.Equal(PasswordPolicy.Rules.EqualsUserName, result.FailedRule);
    }

    [Fact]
    public void Rejects_a_password_equal_to_the_username_in_a_different_case()
    {
        var result = PolicyDenying().Validate(UserName.ToUpperInvariant(), UserName);

        Assert.False(result.IsValid);
        Assert.Equal(PasswordPolicy.Rules.EqualsUserName, result.FailedRule);
    }

    [Fact]
    public void Compares_against_the_trimmed_username()
    {
        var result = PolicyDenying().Validate(UserName, $"  {UserName}  ");

        Assert.False(result.IsValid);
        Assert.Equal(PasswordPolicy.Rules.EqualsUserName, result.FailedRule);
    }

    // Missing input

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Rejects_a_missing_password(string? password)
    {
        var result = PolicyDenying().Validate(password, UserName);

        Assert.False(result.IsValid);
        Assert.Equal(PasswordPolicy.Rules.Required, result.FailedRule);
    }

    // FR-5.7 — the reason is actionable, and never echoes the password

    [Fact]
    public void States_a_rule_and_a_reason_on_every_rejection()
    {
        var result = PolicyDenying().Validate("short", UserName);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.FailedRule));
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("onthedenylist12")]
    [InlineData(UserName)]
    public void Never_echoes_the_submitted_password_in_the_reason(string password)
    {
        var result = PolicyDenying("onthedenylist12").Validate(password, UserName);

        Assert.False(result.IsValid);
        Assert.DoesNotContain(password, result.Reason!, StringComparison.OrdinalIgnoreCase);
    }
}
