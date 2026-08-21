using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mizrachi.Application.Abstractions;
using Mizrachi.Domain;
using Mizrachi.Infrastructure;
using Mizrachi.Infrastructure.Persistence;

namespace Mizrachi.Tests.Unit.Persistence;

/// <summary>
/// NFR-1.2: the durable store keeps data across a restart. Two independent service providers
/// over one file stand in for stopping and starting the process.
/// </summary>
public sealed class SqliteDurabilityTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "mizrachi-tests", $"{Guid.NewGuid():N}.db");

    private ServiceProvider BuildHost()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = PersistenceOptions.Providers.Sqlite,
                ["Persistence:FilePath"] = _databasePath,
                ["Jwt:Issuer"] = "tests",
                ["Jwt:Audience"] = "tests",
                ["Jwt:SigningKey"] = "a-test-only-signing-key-of-at-least-32-bytes"
            })
            .Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        services.GetRequiredService<IDatabaseInitializer>()
            .InitializeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return services;
    }

    [Fact]
    public async Task Keeps_a_user_across_a_restart()
    {
        var userId = Guid.NewGuid();

        await using (var first = BuildHost())
        {
            var repository = first.GetRequiredService<IUserRepository>();
            Assert.True(await repository.TryAddAsync(
                User.Create(userId, "alice", "hashed:alice"),
                CancellationToken.None));
        }

        await using var second = BuildHost();
        var found = await second.GetRequiredService<IUserRepository>()
            .FindByIdAsync(userId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("alice", found!.UserName);
    }

    [Fact]
    public async Task Keeps_the_username_taken_across_a_restart()
    {
        await using (var first = BuildHost())
        {
            await first.GetRequiredService<IUserRepository>().TryAddAsync(
                User.Create(Guid.NewGuid(), "alice", "hashed:alice"),
                CancellationToken.None);
        }

        await using var second = BuildHost();
        var added = await second.GetRequiredService<IUserRepository>().TryAddAsync(
            User.Create(Guid.NewGuid(), "ALICE", "hashed:alice"),
            CancellationToken.None);

        Assert.False(added);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(_databasePath + suffix);
            }
            catch (IOException)
            {
                // Throwaway temp file; see SqliteUserRepositoryTests.DisposeStore.
            }
        }
    }
}
