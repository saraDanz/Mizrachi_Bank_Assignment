namespace Mizrachi.Domain;

/// <summary>
/// A user account. The schema is exactly three fields, as the specification requires.
/// </summary>
/// <remarks>
/// <see cref="UserPassword"/> holds a password <b>hash</b>. The field name comes from the
/// specified schema and cannot be changed, which is precisely why it is called out here: a
/// plaintext password is never assigned to it, never stored, and never returned (FR-1.4).
/// </remarks>
public sealed class User
{
    private User(Guid userId, string userName, string userPassword)
    {
        UserId = userId;
        UserName = userName;
        UserPassword = userPassword;
    }

    /// <summary>Server-generated identifier. Never accepted from a client (FR-1.2).</summary>
    public Guid UserId { get; }

    /// <summary>Stored trimmed, in the casing the caller chose. Compared case-insensitively.</summary>
    public string UserName { get; }

    /// <summary>The hash produced by the configured password hasher. Never plaintext.</summary>
    public string UserPassword { get; }

    /// <param name="passwordHash">
    /// A hash, never a plaintext password. The parameter is named for what it must contain.
    /// </param>
    public static User Create(Guid userId, string userName, string passwordHash)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId must be a server-generated identifier.", nameof(userId));
        }

        // Trimming here rather than trusting the caller keeps FR-1.6 a property of the type:
        // a User cannot exist with a username that would compare differently once trimmed.
        var normalizedUserName = UserNamePolicy.Normalize(userName);
        if (normalizedUserName.Length == 0)
        {
            throw new ArgumentException("UserName is required.", nameof(userName));
        }

        if (string.IsNullOrEmpty(passwordHash))
        {
            throw new ArgumentException("A password hash is required.", nameof(passwordHash));
        }

        return new User(userId, normalizedUserName, passwordHash);
    }
}
