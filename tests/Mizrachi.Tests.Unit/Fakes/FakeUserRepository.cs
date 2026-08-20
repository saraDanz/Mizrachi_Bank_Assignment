using System.Collections.Concurrent;
using Mizrachi.Application.Abstractions;
using Mizrachi.Domain;

namespace Mizrachi.Tests.Unit.Fakes;

/// <summary>
/// Hand-written in-memory repository that also records how it was called, so a test can assert
/// that a code path did <em>not</em> reach the datastore.
/// </summary>
internal sealed class FakeUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<string, User> _byUserName = new(StringComparer.OrdinalIgnoreCase);

    internal int FindByUserNameCalls { get; private set; }

    internal int FindByIdCalls { get; private set; }

    internal int TryAddCalls { get; private set; }

    internal int DeleteCalls { get; private set; }

    /// <summary>
    /// Seam for FR-1.8: makes the next insert lose the race, as a datastore unique constraint
    /// would when another request won.
    /// </summary>
    internal bool NextAddLosesTheRace { get; set; }

    internal void Seed(User user) => _byUserName[user.UserName] = user;

    public Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        FindByUserNameCalls++;
        _byUserName.TryGetValue(userName, out var user);
        return Task.FromResult<User?>(user);
    }

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        FindByIdCalls++;
        var user = _byUserName.Values.FirstOrDefault(candidate => candidate.UserId == userId);
        return Task.FromResult(user);
    }

    public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken)
    {
        TryAddCalls++;

        if (NextAddLosesTheRace)
        {
            NextAddLosesTheRace = false;
            return Task.FromResult(false);
        }

        return Task.FromResult(_byUserName.TryAdd(user.UserName, user));
    }

    public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        DeleteCalls++;

        var existing = _byUserName.Values.FirstOrDefault(candidate => candidate.UserId == userId);
        if (existing is null)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_byUserName.TryRemove(existing.UserName, out _));
    }
}
