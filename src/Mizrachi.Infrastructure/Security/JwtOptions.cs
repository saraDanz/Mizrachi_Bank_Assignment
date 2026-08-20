using System.ComponentModel.DataAnnotations;

namespace Mizrachi.Infrastructure.Security;

/// <summary>
/// Settings for the credential issued after a successful validation (FR-3.3).
/// </summary>
/// <remarks>
/// <see cref="SigningKey"/> has no default and never appears in a committed file. It comes from
/// user-secrets or an environment variable, and its absence fails at startup (NFR-1.4, NFR-2.6).
/// </remarks>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>256 bits, the minimum for HMAC-SHA256.</summary>
    public const int MinimumSigningKeyBytes = 32;

    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Short by design: there is no revocation path in this scope (§4.7, OQ-4).</summary>
    [Range(1, 60)]
    public int LifetimeMinutes { get; set; } = 15;

    [Required(AllowEmptyStrings = false)]
    public string SigningKey { get; set; } = string.Empty;
}
