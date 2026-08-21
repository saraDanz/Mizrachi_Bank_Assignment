using Mizrachi.Domain;
using Mizrachi.Infrastructure.Security;

namespace Mizrachi.Tests.Unit.Infrastructure;

public sealed class EmbeddedPasswordDenyListTests
{
    private readonly EmbeddedPasswordDenyList _denyList = new();

    [Fact]
    public void Loads_the_embedded_resource()
    {
        Assert.True(_denyList.Count > 0);
    }

    [Fact]
    public void Skips_comments_and_blank_lines()
    {
        Assert.False(_denyList.Contains("#"));
        Assert.False(_denyList.Contains(string.Empty));
    }

    [Theory]
    [InlineData("passwordpassword")]
    [InlineData("correcthorsebatterystaple")]
    [InlineData("PasswordPassword")]
    public void Denies_a_listed_password_regardless_of_casing(string password)
    {
        Assert.True(_denyList.Contains(password));
    }

    [Fact]
    public void Allows_a_password_that_is_not_listed()
    {
        Assert.False(_denyList.Contains("an-unremarkable-passphrase"));
    }

    [Fact]
    public void Contains_only_entries_long_enough_to_be_reachable()
    {
        // An entry shorter than the minimum length could never be submitted anyway (FR-5.1),
        // so its presence would mean the list is padded with rules that can never fire.
        var policy = new PasswordPolicy(_denyList);

        Assert.Equal(
            PasswordPolicy.Rules.CommonlyUsed,
            policy.Validate("passwordpassword", "someone").FailedRule);
    }
}
