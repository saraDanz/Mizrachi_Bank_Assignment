using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Mizrachi.Infrastructure.Security;
using Mizrachi.Tests.Unit.Fakes;

namespace Mizrachi.Tests.Unit.Infrastructure;

public sealed class JwtTokenIssuerTests
{
    private const string SigningKey = "a-test-only-signing-key-of-at-least-32-bytes";

    private readonly FakeClock _clock = new();
    private readonly Guid _userId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private JwtTokenIssuer CreateIssuer(int lifetimeMinutes = 15, string signingKey = SigningKey) =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "mizrachi-tests",
            Audience = "mizrachi-tests",
            LifetimeMinutes = lifetimeMinutes,
            SigningKey = signingKey
        }),
        _clock);

    private JwtSecurityToken Decode(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    [Fact]
    public void Issues_a_token_naming_the_authenticated_subject()
    {
        var issued = CreateIssuer().Issue(_userId, "alice");

        var decoded = Decode(issued.Token);
        Assert.Equal(_userId.ToString(), decoded.Subject);
    }

    [Fact]
    public void Expires_after_the_configured_lifetime()
    {
        var issued = CreateIssuer(lifetimeMinutes: 15).Issue(_userId, "alice");

        Assert.Equal(_clock.UtcNow.AddMinutes(15), issued.ExpiresAt);
    }

    [Fact]
    public void Never_issues_a_token_without_an_expiry()
    {
        var decoded = Decode(CreateIssuer().Issue(_userId, "alice").Token);

        Assert.True(decoded.ValidTo > DateTime.MinValue);
    }

    [Fact]
    public void Carries_no_password_or_hash_in_any_claim()
    {
        // FR-1.4 applies to the token as much as to a response body.
        var decoded = Decode(CreateIssuer().Issue(_userId, "alice").Token);

        foreach (var claim in decoded.Claims)
        {
            Assert.DoesNotContain("password", claim.Type, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", claim.Type, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Signs_with_a_pinned_algorithm_and_never_none()
    {
        var decoded = Decode(CreateIssuer().Issue(_userId, "alice").Token);

        Assert.Equal("HS256", decoded.Header.Alg);
        Assert.NotEqual("none", decoded.Header.Alg, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Issues_a_distinct_token_each_time()
    {
        var issuer = CreateIssuer();

        Assert.NotEqual(issuer.Issue(_userId, "alice").Token, issuer.Issue(_userId, "alice").Token);
    }

    [Fact]
    public void Refuses_a_signing_key_shorter_than_256_bits()
    {
        var tooShort = new string('k', JwtOptions.MinimumSigningKeyBytes - 1);

        Assert.Throws<InvalidOperationException>(() => CreateIssuer(signingKey: tooShort));
    }
}
