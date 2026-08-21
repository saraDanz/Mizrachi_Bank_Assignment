namespace Mizrachi.Application.Abstractions;

/// <summary>
/// Password hashing, behind our own abstraction so no other layer names a hashing library
/// (NFR-2.2).
/// </summary>
public interface IPasswordHasher
{
    /// <returns>An encoded hash, carrying its own salt and parameters. Never the password.</returns>
    string Hash(string password);

    PasswordVerification Verify(string passwordHash, string password);
}

/// <summary>
/// The outcome of verifying a password against a stored hash.
/// </summary>
public enum PasswordVerification
{
    Failed = 0,

    Success = 1,

    /// <summary>
    /// Correct, but the stored hash uses weaker parameters than the current policy. The caller
    /// may re-hash inside the successful path; it must never treat this as a failure.
    /// </summary>
    SuccessRehashNeeded = 2
}
