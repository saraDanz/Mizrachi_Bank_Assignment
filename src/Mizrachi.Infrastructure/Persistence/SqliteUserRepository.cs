using Microsoft.EntityFrameworkCore;
using Mizrachi.Application.Abstractions;
using Mizrachi.Domain;

namespace Mizrachi.Infrastructure.Persistence;

/// <summary>
/// Durable store backed by a SQLite file, so data survives a restart with no database software
/// installed (NFR-1.1, NFR-1.2).
/// </summary>
internal sealed class SqliteUserRepository : IUserRepository
{
    private readonly IDbContextFactory<UsersDbContext> _contextFactory;

    public SqliteUserRepository(IDbContextFactory<UsersDbContext> contextFactory) =>
        _contextFactory = contextFactory;

    public async Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // EF.Functions.Like is not used here: the column collation is NOCASE, so an equality
        // comparison is already case-insensitive, and it can use the unique index.
        return await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.UserName == userName, cancellationToken);
    }

    public async Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);
    }

    public async Task<bool> TryAddAsync(User user, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        context.Users.Add(user);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            // The database decided this, not a prior check (FR-1.8). Under concurrent inserts
            // of one username, every loser lands here and exactly one caller sees success.
            return false;
        }
    }

    public async Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // A single DELETE ... WHERE, so the row count decides the outcome. Two concurrent
        // deletes of one id cannot both report success (FR-2.6).
        var deleted = await context.Users
            .Where(user => user.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    /// <remarks>
    /// SQLite reports a unique-index violation as extended result code 2067 (constraint
    /// violation, unique). Matching on the code rather than the message keeps this independent
    /// of locale and of provider message wording.
    /// </remarks>
    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteExtendedErrorCode: 2067 };
}
