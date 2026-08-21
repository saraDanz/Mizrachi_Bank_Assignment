using Mizrachi.Application.Abstractions;

namespace Mizrachi.Tests.Unit.Fakes;

/// <summary>Issues identifiers a test chose in advance.</summary>
internal sealed class FakeIdGenerator : IIdGenerator
{
    private readonly Queue<Guid> _queued = new();

    internal FakeIdGenerator(params Guid[] ids)
    {
        foreach (var id in ids)
        {
            _queued.Enqueue(id);
        }
    }

    public Guid NewId() => _queued.Count > 0 ? _queued.Dequeue() : Guid.NewGuid();
}

/// <summary>A clock that does not move unless a test moves it.</summary>
internal sealed class FakeClock : IClock
{
    internal FakeClock(DateTimeOffset? now = null) =>
        UtcNow = now ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; private set; }

    internal void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>Issues a predictable token, so tests never depend on real signing.</summary>
internal sealed class FakeTokenIssuer : ITokenIssuer
{
    internal int IssueCount { get; private set; }

    public IssuedToken Issue(Guid userId, string userName)
    {
        IssueCount++;
        return new IssuedToken($"token-for-{userId:N}", new DateTimeOffset(2026, 1, 1, 0, 15, 0, TimeSpan.Zero));
    }
}

/// <summary>
/// Records the security events raised. Note what it cannot record for a failed
/// authentication: <see cref="ISecurityEventLog.AuthenticationFailed"/> carries no username,
/// so there is nothing here to assert about one (NFR-2.3).
/// </summary>
internal sealed class RecordingSecurityEventLog : ISecurityEventLog
{
    internal List<Guid> UsersCreated { get; } = new();

    internal List<Guid> UsersDeleted { get; } = new();

    internal List<Guid> AuthenticationSuccesses { get; } = new();

    internal int AuthenticationFailures { get; private set; }

    internal List<(Guid CallerId, Guid TargetUserId)> AuthorizationRefusals { get; } = new();

    public void UserCreated(Guid userId) => UsersCreated.Add(userId);

    public void UserDeleted(Guid userId) => UsersDeleted.Add(userId);

    public void AuthenticationSucceeded(Guid userId) => AuthenticationSuccesses.Add(userId);

    public void AuthenticationFailed() => AuthenticationFailures++;

    public void AuthorizationRefused(Guid callerId, Guid targetUserId) =>
        AuthorizationRefusals.Add((callerId, targetUserId));
}
