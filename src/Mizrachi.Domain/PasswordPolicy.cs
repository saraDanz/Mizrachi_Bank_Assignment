namespace Mizrachi.Domain;

/// <summary>
/// The password policy of §1.5 of the requirements: length-bounded, deny-listed, and
/// deliberately free of composition rules (FR-5.1–5.7).
/// </summary>
public sealed class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    public static class Rules
    {
        public const string Required = "password_required";
        public const string TooShort = "password_too_short";
        public const string TooLong = "password_too_long";
        public const string EqualsUserName = "password_equals_username";
        public const string CommonlyUsed = "password_commonly_used";
    }

    private readonly IPasswordDenyList _denyList;

    public PasswordPolicy(IPasswordDenyList denyList)
    {
        _denyList = denyList ?? throw new ArgumentNullException(nameof(denyList));
    }

    /// <remarks>
    /// There is no check for uppercase, digits or symbols, and that is not an omission:
    /// composition rules narrow the search space rather than widen it, because users satisfy
    /// them predictably (FR-5.4, REQUIREMENTS §3.3). Length and a deny-list do the work.
    /// </remarks>
    public PolicyResult Validate(string? password, string? userName)
    {
        if (string.IsNullOrEmpty(password))
        {
            return PolicyResult.Fail(Rules.Required, "Password is required.");
        }

        if (password.Length < MinLength)
        {
            return PolicyResult.Fail(Rules.TooShort, $"Password must be at least {MinLength} characters.");
        }

        // The maximum is a bound on server work, not a storage limit (FR-5.2). It is checked
        // here, before the password can reach an iterated hash function.
        if (password.Length > MaxLength)
        {
            return PolicyResult.Fail(Rules.TooLong, $"Password must be at most {MaxLength} characters.");
        }

        if (string.Equals(password, UserNamePolicy.Normalize(userName), StringComparison.OrdinalIgnoreCase))
        {
            return PolicyResult.Fail(Rules.EqualsUserName, "Password must not be the same as the username.");
        }

        if (_denyList.Contains(password))
        {
            return PolicyResult.Fail(Rules.CommonlyUsed, "Password is on the list of commonly used passwords.");
        }

        return PolicyResult.Ok();
    }
}
