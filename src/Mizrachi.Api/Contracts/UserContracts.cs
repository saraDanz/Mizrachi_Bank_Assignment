using System.ComponentModel.DataAnnotations;
using Mizrachi.Domain;

namespace Mizrachi.Api.Contracts;

/// <summary>
/// Credentials for creating an account. There is no <c>UserId</c> member, so a client-supplied
/// identifier has nowhere to bind (FR-1.2).
/// </summary>
public sealed class CreateUserRequest
{
    [Required(AllowEmptyStrings = false)]
    public string UserName { get; set; } = string.Empty;

    /// <remarks>
    /// No length attribute here on purpose: <see cref="PasswordPolicy"/> owns the bounds so the
    /// rejection can name the rule that failed (FR-5.7), and it runs before the hasher.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Overridden so an interpolated log line or an exception dump cannot spill the password
    /// (NFR-2.3). Nothing about this object is safe to render.
    /// </summary>
    public override string ToString() => nameof(CreateUserRequest);
}

/// <summary>
/// Credentials to validate. Body only — never a query string or route value (FR-3.2).
/// </summary>
public sealed class ValidateUserRequest
{
    [Required(AllowEmptyStrings = false)]
    public string UserName { get; set; } = string.Empty;

    /// <remarks>
    /// Bounded here because validation has no password policy to appeal to, and the cost of an
    /// iterated hash must not depend on how much the caller sent (FR-5.2).
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    [StringLength(PasswordPolicy.MaxLength)]
    public string Password { get; set; } = string.Empty;

    public override string ToString() => nameof(ValidateUserRequest);
}

/// <summary>The created account. No password, in any form (FR-1.4).</summary>
public sealed record CreateUserResponse(Guid UserId, string UserName);

/// <summary>The validated account and the credential authorising a later delete (FR-3.3).</summary>
public sealed record ValidateUserResponse(Guid UserId, string UserName, string Token, DateTimeOffset ExpiresAt);
