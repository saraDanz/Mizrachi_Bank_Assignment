using Mizrachi.Domain;

namespace Mizrachi.Tests.Unit.Fakes;

/// <summary>
/// Hand-written stand-in for the deny list. No mocking library, per CLAUDE.md.
/// </summary>
internal sealed class StubPasswordDenyList : IPasswordDenyList
{
    private readonly HashSet<string> _denied;

    internal StubPasswordDenyList(params string[] denied) =>
        _denied = new HashSet<string>(denied, StringComparer.OrdinalIgnoreCase);

    public bool Contains(string password) => _denied.Contains(password);
}
