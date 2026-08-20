using System.Collections.Concurrent;
using Mizrachi.Application.Abstractions;
using Mizrachi.Domain;

namespace Mizrachi.Infrastructure.Persistence;

/// <summary>
/// Volatile store, so the API runs on a clean machine with nothing installed (NFR-1.1).
/// </summary>
/// <remarks>
/// Uniqueness comes from <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/>, which is
/// atomic: under simultaneous requests for the same username exactly one insert wins and the
/// others are told they lost (FR-1.8). Nothing here checks for existence first.
/// </remarks>
internal sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<string, User> _byUserName = new(UserNameComparer.Instance);
    private readonly ConcurrentDictionary<Guid, string> _userNamesById = new();

    public Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        _byUserName.TryGetValue(userName, out var user);
        return Task.FromResult<User?>(user);
    }

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_userNamesById.TryGetValue(userId, out var userName) &&
            _byUserName.TryGetValue(userName, out var user))
        {
            return Task.FromResult<User?>(user);
        }

        return Task.FromResult<User?>(null);
    }

    public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken)
    {
        if (!_byUserName.TryAdd(user.UserName, user))
        {
            return Task.FromResult(false);
        }

        _userNamesById[user.UserId] = user.UserName;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Removing the id is the gate, so exactly one caller can win a concurrent double delete
        // and the loser is told the account was not there (FR-2.6).
        if (!_userNamesById.TryRemove(userId, out var userName))
        {
            return Task.FromResult(false);
        }

        _byUserName.TryRemove(userName, out _);
        return Task.FromResult(true);
    }
}
