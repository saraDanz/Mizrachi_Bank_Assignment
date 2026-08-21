using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Mizrachi.Application.Abstractions;

namespace Mizrachi.Infrastructure.Security;

/// <summary>
/// Issues a short-lived signed credential naming the account that authenticated (FR-3.3).
/// </summary>
/// <remarks>
/// The token carries the subject, the username and its lifetime — no password, no hash, and
/// nothing else. The subject is what the delete endpoint uses to decide ownership, so it must
/// come from here rather than from anything the client can set.
/// </remarks>
public sealed class JwtTokenIssuer : ITokenIssuer
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenIssuer(IOptions<JwtOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        if (keyBytes.Length < JwtOptions.MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                $"The JWT signing key must be at least {JwtOptions.MinimumSigningKeyBytes} bytes.");
        }

        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);
    }

    public IssuedToken Issue(Guid userId, string userName)
    {
        var issuedAt = _clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.LifetimeMinutes);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, userName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            },
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _signingCredentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
