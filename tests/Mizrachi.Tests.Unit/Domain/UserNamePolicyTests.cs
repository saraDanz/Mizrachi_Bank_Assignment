using Mizrachi.Domain;

namespace Mizrachi.Tests.Unit.Domain;

public sealed class UserNamePolicyTests
{
    private readonly UserNamePolicy _policy = new();

    [Theory]
    [InlineData("abc")]                     // minimum length
    [InlineData("alice")]
    [InlineData("alice.smith")]
    [InlineData("a_b-c.d")]
    [InlineData("user123")]
    [InlineData("9lives")]                  // may start with a digit
    public void Accepts_a_well_formed_username(string userName)
    {
        var result = _policy.Validate(userName);

        Assert.True(result.IsValid);
        Assert.Null(result.FailedRule);
    }

    [Fact]
    public void Accepts_the_maximum_length()
    {
        var result = _policy.Validate(new string('a', UserNamePolicy.MaxLength));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rejects_one_character_over_the_maximum()
    {
        var result = _policy.Validate(new string('a', UserNamePolicy.MaxLength + 1));

        Assert.False(result.IsValid);
        Assert.Equal(UserNamePolicy.Rules.TooLong, result.FailedRule);
    }

    [Theory]
    [InlineData("ab")]                      // one under the minimum
    [InlineData("a")]
    public void Rejects_a_username_under_the_minimum(string userName)
    {
        var result = _policy.Validate(userName);

        Assert.False(result.IsValid);
        Assert.Equal(UserNamePolicy.Rules.TooShort, result.FailedRule);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_username(string? userName)
    {
        var result = _policy.Validate(userName);

        Assert.False(result.IsValid);
        Assert.Equal(UserNamePolicy.Rules.Required, result.FailedRule);
    }

    [Theory]
    [InlineData(".alice")]
    [InlineData("_alice")]
    [InlineData("-alice")]
    public void Rejects_a_username_not_starting_with_a_letter_or_digit(string userName)
    {
        var result = _policy.Validate(userName);

        Assert.False(result.IsValid);
        Assert.Equal(UserNamePolicy.Rules.InvalidStart, result.FailedRule);
    }

    [Theory]
    [InlineData("alice smith")]             // space
    [InlineData("alice@example.com")]
    [InlineData("alice+tag")]
    [InlineData("alice/../etc")]
    [InlineData("élodie")]                  // non-ASCII: see the ASCII note on the policy
    [InlineData("אליס")]
    public void Rejects_characters_outside_the_permitted_set(string userName)
    {
        var result = _policy.Validate(userName);

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailedRule);
    }

    [Theory]
    [InlineData("  alice  ", "alice")]
    [InlineData("\talice\n", "alice")]
    [InlineData("alice", "alice")]
    [InlineData(null, "")]
    public void Normalize_trims_surrounding_whitespace_and_nothing_else(string? input, string expected)
    {
        Assert.Equal(expected, UserNamePolicy.Normalize(input));
    }

    [Fact]
    public void Accepts_a_username_that_is_well_formed_once_trimmed()
    {
        var result = _policy.Validate("   alice.smith   ");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Preserves_casing_when_normalizing()
    {
        // Uniqueness ignores case (FR-1.5), but storage keeps what the caller chose.
        Assert.Equal("Alice", UserNamePolicy.Normalize("  Alice  "));
    }

    [Fact]
    public void States_a_rule_and_a_reason_on_every_rejection()
    {
        var result = _policy.Validate("ab");

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.FailedRule));
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }
}
