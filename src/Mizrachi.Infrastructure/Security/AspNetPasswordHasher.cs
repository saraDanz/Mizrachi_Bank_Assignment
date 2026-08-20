using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Mizrachi.Application.Abstractions;
using Mizrachi.Domain;

namespace Mizrachi.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA512 password hashing, delegated to the framework's vetted implementation
/// (NFR-2.1, NFR-2.2). Nothing here computes a hash by hand.
/// </summary>
/// <remarks>
/// The stored format is self-describing: the marker byte, PRF, iteration count and salt all
/// travel inside the encoded string. Raising <see cref="IterationCount"/> later therefore needs
/// no migration — existing hashes keep verifying, and <see cref="Verify"/> reports
/// <see cref="PasswordVerification.SuccessRehashNeeded"/> so the caller can upgrade the stored
/// value inside a successful login.
/// </remarks>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    /// <summary>
    /// 210,000 — the OWASP figure for PBKDF2-HMAC-SHA512. Set explicitly because the framework
    /// default is 100,000, which is lower than current guidance.
    /// </summary>
    public const int IterationCount = 210_000;

    private readonly PasswordHasher<User> _hasher;

    public AspNetPasswordHasher()
    {
        _hasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = IterationCount
        }));
    }

    /// <remarks>
    /// The <c>user</c> argument the framework wants takes no part in the hash — it exists for
    /// callers that key on the user. Passing a placeholder keeps the salt per-hash and random,
    /// which is what NFR-2.1 requires.
    /// </remarks>
    public string Hash(string password) => _hasher.HashPassword(PlaceholderUser, password);

    public PasswordVerification Verify(string passwordHash, string password)
    {
        try
        {
            return _hasher.VerifyHashedPassword(PlaceholderUser, passwordHash, password) switch
            {
                PasswordVerificationResult.Success => PasswordVerification.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
                _ => PasswordVerification.Failed
            };
        }
        catch (FormatException)
        {
            // A stored value that is not valid base64 makes the framework throw rather than
            // report failure. Failing closed keeps the outcome a plain rejection: an account
            // whose stored hash is corrupt must not answer differently — with a 500 where every
            // other rejection is a 401 — from one whose password is merely wrong (FR-3.5).
            return PasswordVerification.Failed;
        }
    }

    private static readonly User PlaceholderUser =
        User.Create(Guid.Parse("00000000-0000-0000-0000-0000000000ff"), "hasher", "unused");
}
