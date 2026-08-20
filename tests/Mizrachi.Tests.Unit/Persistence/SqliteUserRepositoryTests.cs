using Microsoft.Extensions.DependencyInjection;
using Mizrachi.Infrastructure.Persistence;

namespace Mizrachi.Tests.Unit.Persistence;

/// <summary>
/// The contract suite, run against the SQLite store on a throwaway file per test.
/// </summary>
/// <remarks>
/// Nothing here names an EF or SQLite type: the store is selected by configuration and prepared
/// through <see cref="IDatabaseInitializer"/>, exactly as the API does it.
/// </remarks>
public sealed class SqliteUserRepositoryTests : UserRepositoryContractTests
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "mizrachi-tests", $"{Guid.NewGuid():N}.db");

    protected override IReadOnlyDictionary<string, string?> ProviderConfiguration() =>
        new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = PersistenceOptions.Providers.Sqlite,
            ["Persistence:FilePath"] = _databasePath
        };

    protected override void OnStoreCreated(IServiceProvider services) =>
        services.GetRequiredService<IDatabaseInitializer>()
            .InitializeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    protected override void DisposeStore()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(_databasePath + suffix);
            }
            catch (IOException)
            {
                // Connection pooling can still hold the file when the suite finishes. These are
                // throwaway files under the temp directory, so leaving one behind is preferable
                // to referencing the SQLite driver here just to flush its pool.
            }
        }
    }
}
