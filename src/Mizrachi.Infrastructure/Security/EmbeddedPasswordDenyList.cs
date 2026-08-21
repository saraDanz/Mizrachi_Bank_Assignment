using System.Reflection;
using Mizrachi.Domain;

namespace Mizrachi.Infrastructure.Security;

/// <summary>
/// The deny list of FR-5.5, read once from an embedded resource.
/// </summary>
/// <remarks>
/// A representative list, not a breach corpus. Screening against a real breached-password
/// service with a privacy-preserving lookup is out of scope and recorded as such
/// (REQUIREMENTS §4.9).
/// </remarks>
public sealed class EmbeddedPasswordDenyList : IPasswordDenyList
{
    private const string ResourceName = "Mizrachi.Infrastructure.Security.common-passwords.txt";

    private readonly HashSet<string> _denied;

    public EmbeddedPasswordDenyList()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");

        using var reader = new StreamReader(stream);

        _denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (reader.ReadLine() is { } line)
        {
            var entry = line.Trim();
            if (entry.Length > 0 && !entry.StartsWith('#'))
            {
                _denied.Add(entry);
            }
        }
    }

    public int Count => _denied.Count;

    public bool Contains(string password) => _denied.Contains(password);
}
