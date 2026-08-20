namespace Mizrachi.Domain;

/// <summary>
/// A list of commonly used passwords that must be refused (FR-5.5).
/// </summary>
/// <remarks>
/// Declared here because <see cref="PasswordPolicy"/> depends on it, but implemented outside
/// the Domain: loading a list is I/O, and this project holds no I/O and no packages.
/// </remarks>
public interface IPasswordDenyList
{
    bool Contains(string password);
}
