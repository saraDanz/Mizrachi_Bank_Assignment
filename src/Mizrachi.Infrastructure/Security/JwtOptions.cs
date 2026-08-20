namespace Mizrachi.Infrastructure.Security;

/// <summary>
/// Settings for the credential issued after a successful validation (FR-3.3).
/// </summary>
/// <remarks>
/// <see cref="SigningKey"/> has no default and never appears in a committed file. It comes from
/// user-secrets or an environment variable, and its absence fails at startup (NFR-1.4, NFR-2.6).
/// The one exception is Development, where <see cref="EphemeralDevelopmentSigningKey"/> fills it
/// with a random in-memory value that lasts as long as the process (NFR-1.5).
///
/// These members carry no validation attributes on purpose. <see cref="JwtOptionsValidator"/> is
/// the single authority, because a data-annotation failure can only report that a member is
/// required — it cannot name the configuration key or the command that sets it.
/// </remarks>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>256 bits, the minimum for HMAC-SHA256.</summary>
    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>Short by design: there is no revocation path in this scope (§4.7, OQ-4).</summary>
    public int LifetimeMinutes { get; set; } = 15;

    public string SigningKey { get; set; } = string.Empty;
}
