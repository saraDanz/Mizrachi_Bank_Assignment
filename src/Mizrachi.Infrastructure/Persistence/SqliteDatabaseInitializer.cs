using Microsoft.EntityFrameworkCore;

namespace Mizrachi.Infrastructure.Persistence;

/// <summary>
/// Prepares a store before the first request. Public so the API can call it without naming any
/// EF type.
/// </summary>
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

internal sealed class SqliteDatabaseInitializer : IDatabaseInitializer
{
    private readonly IDbContextFactory<UsersDbContext> _contextFactory;

    public SqliteDatabaseInitializer(IDbContextFactory<UsersDbContext> contextFactory) =>
        _contextFactory = contextFactory;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.EnsureCreatedAsync(cancellationToken);

        // Write-ahead logging lets readers run alongside a writer and turns a contended insert
        // into a constraint decision rather than a lock timeout. Set on the file, so it
        // persists; the busy timeout is per connection.
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=30000;", cancellationToken);
    }
}
