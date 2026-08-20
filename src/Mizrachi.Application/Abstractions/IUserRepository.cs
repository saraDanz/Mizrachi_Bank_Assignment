using Mizrachi.Domain;

namespace Mizrachi.Application.Abstractions;

/// <summary>
/// Persistence port for <see cref="User"/>.
/// </summary>
/// <remarks>
/// There is deliberately no <c>ExistsAsync</c>. Uniqueness is decided by the datastore inside
/// <see cref="TryAddAsync"/>, so the check-then-insert race that FR-1.8 forbids cannot be
/// written against this interface — not "should not be", but cannot.
/// </remarks>
public interface IUserRepository
{
    Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken);

    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <returns>
    /// <c>true</c> when the user was inserted; <c>false</c> when the username is already
    /// taken. Implementations must decide this atomically, never with a prior lookup.
    /// </returns>
    Task<bool> TryAddAsync(User user, CancellationToken cancellationToken);

    /// <returns><c>true</c> when a user was deleted; <c>false</c> when none existed.</returns>
    Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken);
}
