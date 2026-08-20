using System.Text;
using Mizrachi.Application.Abstractions;

namespace Mizrachi.Tests.Unit.Fakes;

/// <summary>
/// Hand-written hasher that counts its calls, so a test can prove that a hash verification
/// happened on the unknown-username path (FR-3.6) and that no hashing happened at all when a
/// password was rejected for length (FR-5.2).
/// </summary>
internal sealed class CountingPasswordHasher : IPasswordHasher
{
    private const string Prefix = "hashed:";

    internal int HashCount { get; private set; }

    internal int VerifyCount { get; private set; }

    internal List<string> VerifiedAgainstHashes { get; } = new();

    public string Hash(string password)
    {
        HashCount++;
        return Encode(password);
    }

    public PasswordVerification Verify(string passwordHash, string password)
    {
        VerifyCount++;
        VerifiedAgainstHashes.Add(passwordHash);

        return passwordHash == Encode(password)
            ? PasswordVerification.Success
            : PasswordVerification.Failed;
    }

    /// <summary>
    /// Deterministic and reversible — it is a test double, not a hash. What matters is that its
    /// output does not contain the plaintext as a substring, so a test asserting "the stored
    /// value is not the password" fails for the right reason when a service stores the wrong
    /// thing, rather than passing because the double happened to embed it.
    /// </summary>
    private static string Encode(string password) =>
        Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
}
