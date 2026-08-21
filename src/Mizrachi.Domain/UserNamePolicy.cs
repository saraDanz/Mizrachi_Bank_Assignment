namespace Mizrachi.Domain;

/// <summary>
/// The permitted shape of a username (OQ-2).
/// </summary>
/// <remarks>
/// The character set is restricted to ASCII deliberately. SQLite's <c>NOCASE</c> collation
/// folds only ASCII, while .NET's <c>OrdinalIgnoreCase</c> folds the full Unicode range — so a
/// non-ASCII username would be "taken" in one store and free in another, and case-insensitive
/// uniqueness (FR-1.5) would depend on which provider happened to be configured. Restricting
/// the charset makes every store agree by construction rather than by luck.
/// </remarks>
public sealed class UserNamePolicy
{
    public const int MinLength = 3;
    public const int MaxLength = 64;

    public static class Rules
    {
        public const string Required = "username_required";
        public const string TooShort = "username_too_short";
        public const string TooLong = "username_too_long";
        public const string InvalidStart = "username_invalid_start";
        public const string InvalidCharacters = "username_invalid_characters";
    }

    public PolicyResult Validate(string? userName)
    {
        var name = Normalize(userName);

        if (name.Length == 0)
        {
            return PolicyResult.Fail(Rules.Required, "Username is required.");
        }

        if (name.Length < MinLength)
        {
            return PolicyResult.Fail(Rules.TooShort, $"Username must be at least {MinLength} characters.");
        }

        if (name.Length > MaxLength)
        {
            return PolicyResult.Fail(Rules.TooLong, $"Username must be at most {MaxLength} characters.");
        }

        if (!IsAsciiAlphanumeric(name[0]))
        {
            return PolicyResult.Fail(Rules.InvalidStart, "Username must start with a letter or a digit.");
        }

        foreach (var character in name)
        {
            if (!IsPermitted(character))
            {
                return PolicyResult.Fail(
                    Rules.InvalidCharacters,
                    "Username may contain only ASCII letters, digits, dot, underscore and hyphen.");
            }
        }

        return PolicyResult.Ok();
    }

    /// <summary>
    /// Trims surrounding whitespace, so that uniqueness is evaluated on the trimmed form
    /// (FR-1.6). Casing is left untouched: it is preserved in storage and ignored only when
    /// comparing.
    /// </summary>
    public static string Normalize(string? userName) => userName?.Trim() ?? string.Empty;

    private static bool IsPermitted(char character) =>
        IsAsciiAlphanumeric(character) || character is '.' or '_' or '-';

    private static bool IsAsciiAlphanumeric(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
}
