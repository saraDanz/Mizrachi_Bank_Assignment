namespace Mizrachi.Domain;

/// <summary>
/// How two usernames are compared for uniqueness (FR-1.5): ordinal, folding case for ASCII
/// letters only.
/// </summary>
/// <remarks>
/// The folding rule is defined here, once, because every store has to agree on it. SQLite's
/// <c>NOCASE</c> collation folds only ASCII, while .NET's <c>OrdinalIgnoreCase</c> folds the
/// whole Unicode range — so <c>ÉLODIE</c> and <c>élodie</c> are the same name to one store and
/// different names to another. Matching SQLite's narrower rule everywhere makes the stores
/// agree by construction, rather than leaving uniqueness dependent on which provider happens to
/// be configured. <see cref="UserNamePolicy"/> keeps non-ASCII usernames out in the first
/// place; this makes the stores consistent even if it ever did not.
/// </remarks>
public sealed class UserNameComparer : IEqualityComparer<string>, IComparer<string>
{
    public static readonly UserNameComparer Instance = new();

    private UserNameComparer()
    {
    }

    public bool Equals(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null || x.Length != y.Length)
        {
            return false;
        }

        for (var index = 0; index < x.Length; index++)
        {
            if (FoldAscii(x[index]) != FoldAscii(y[index]))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(string obj)
    {
        var hash = new HashCode();

        foreach (var character in obj)
        {
            hash.Add(FoldAscii(character));
        }

        return hash.ToHashCode();
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var shared = Math.Min(x.Length, y.Length);

        for (var index = 0; index < shared; index++)
        {
            var difference = FoldAscii(x[index]).CompareTo(FoldAscii(y[index]));
            if (difference != 0)
            {
                return difference;
            }
        }

        return x.Length.CompareTo(y.Length);
    }

    private static char FoldAscii(char character) =>
        character is >= 'A' and <= 'Z' ? (char)(character + 32) : character;
}
