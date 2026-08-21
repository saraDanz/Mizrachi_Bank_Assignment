using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mizrachi.Application.Abstractions;
using Mizrachi.Domain;
using Mizrachi.Infrastructure;

namespace Mizrachi.Tests.Unit.Persistence;

/// <summary>
/// The behaviour every store must exhibit (NFR-3.2). One suite, run against each provider by a
/// subclass; a store that passes here is interchangeable with the others.
/// </summary>
/// <remarks>
/// Subclasses supply only configuration, and the repository is resolved through
/// <see cref="InfrastructureRegistration.AddInfrastructure"/>. That keeps the test project free
/// of any reference to EF or to the internal repository types, and means the suite exercises
/// the same composition path the API uses.
/// </remarks>
public abstract class UserRepositoryContractTests : IDisposable
{
    private readonly ServiceProvider _services;

    protected UserRepositoryContractTests()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "tests",
            ["Jwt:Audience"] = "tests",
            ["Jwt:SigningKey"] = "a-test-only-signing-key-of-at-least-32-bytes"
        };

        foreach (var setting in ProviderConfiguration())
        {
            settings[setting.Key] = setting.Value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        _services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        OnStoreCreated(_services);

        Repository = _services.GetRequiredService<IUserRepository>();
    }

    /// <summary>The <c>Persistence:*</c> settings that select and configure this store.</summary>
    protected abstract IReadOnlyDictionary<string, string?> ProviderConfiguration();

    /// <summary>Hook for stores needing preparation before use, such as creating a schema.</summary>
    protected virtual void OnStoreCreated(IServiceProvider services)
    {
    }

    protected IUserRepository Repository { get; }

    private static User NewUser(string userName, Guid? id = null) =>
        User.Create(id ?? Guid.NewGuid(), userName, "hashed:" + userName);

    [Fact]
    public async Task Stores_and_returns_a_user_by_username()
    {
        var user = NewUser("alice");

        Assert.True(await Repository.TryAddAsync(user, CancellationToken.None));

        var found = await Repository.FindByUserNameAsync("alice", CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(user.UserId, found!.UserId);
        Assert.Equal(user.UserPassword, found.UserPassword);
    }

    [Fact]
    public async Task Stores_and_returns_a_user_by_id()
    {
        var user = NewUser("alice");
        await Repository.TryAddAsync(user, CancellationToken.None);

        var found = await Repository.FindByIdAsync(user.UserId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("alice", found!.UserName);
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_username()
    {
        Assert.Null(await Repository.FindByUserNameAsync("nobody", CancellationToken.None));
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_id()
    {
        Assert.Null(await Repository.FindByIdAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Theory]
    [InlineData("alice", "ALICE")]
    [InlineData("alice", "Alice")]
    [InlineData("AlIcE", "aLiCe")]
    public async Task Finds_a_user_regardless_of_case(string stored, string queried)
    {
        await Repository.TryAddAsync(NewUser(stored), CancellationToken.None);

        Assert.NotNull(await Repository.FindByUserNameAsync(queried, CancellationToken.None));
    }

    [Theory]
    [InlineData("alice", "ALICE")]
    [InlineData("alice", "Alice")]
    public async Task Refuses_a_username_that_differs_only_by_case(string first, string second)
    {
        // FR-1.5: uniqueness is case-insensitive.
        Assert.True(await Repository.TryAddAsync(NewUser(first), CancellationToken.None));

        Assert.False(await Repository.TryAddAsync(NewUser(second), CancellationToken.None));
    }

    [Fact]
    public async Task Agrees_with_every_other_store_on_non_ascii_case_folding()
    {
        // The folding rule is ASCII-only and defined once, in UserNameComparer, so that every
        // store gives the same answer here. Without that, .NET's OrdinalIgnoreCase would call
        // these the same name while SQLite's NOCASE would not.
        Assert.True(await Repository.TryAddAsync(NewUser("Élodie"), CancellationToken.None));

        var accentedLower = await Repository.FindByUserNameAsync("élodie", CancellationToken.None);
        var addedSeparately = await Repository.TryAddAsync(NewUser("élodie"), CancellationToken.None);

        Assert.Null(accentedLower);
        Assert.True(addedSeparately);
    }

    [Fact]
    public async Task Refuses_a_duplicate_username()
    {
        Assert.True(await Repository.TryAddAsync(NewUser("alice"), CancellationToken.None));

        Assert.False(await Repository.TryAddAsync(NewUser("alice"), CancellationToken.None));
    }

    [Fact]
    public async Task Keeps_the_first_user_when_a_duplicate_is_refused()
    {
        var first = NewUser("alice");
        await Repository.TryAddAsync(first, CancellationToken.None);

        await Repository.TryAddAsync(NewUser("alice"), CancellationToken.None);

        var found = await Repository.FindByUserNameAsync("alice", CancellationToken.None);
        Assert.Equal(first.UserId, found!.UserId);
    }

    [Fact]
    public async Task Deletes_a_user()
    {
        var user = NewUser("alice");
        await Repository.TryAddAsync(user, CancellationToken.None);

        Assert.True(await Repository.DeleteAsync(user.UserId, CancellationToken.None));
        Assert.Null(await Repository.FindByIdAsync(user.UserId, CancellationToken.None));
        Assert.Null(await Repository.FindByUserNameAsync("alice", CancellationToken.None));
    }

    [Fact]
    public async Task Reports_a_delete_of_an_unknown_id_as_not_deleted()
    {
        Assert.False(await Repository.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Reports_a_repeat_delete_as_not_deleted()
    {
        var user = NewUser("alice");
        await Repository.TryAddAsync(user, CancellationToken.None);
        await Repository.DeleteAsync(user.UserId, CancellationToken.None);

        Assert.False(await Repository.DeleteAsync(user.UserId, CancellationToken.None));
    }

    [Fact]
    public async Task Frees_the_username_after_a_delete()
    {
        var user = NewUser("alice");
        await Repository.TryAddAsync(user, CancellationToken.None);
        await Repository.DeleteAsync(user.UserId, CancellationToken.None);

        Assert.True(await Repository.TryAddAsync(NewUser("alice"), CancellationToken.None));
    }

    [Fact]
    public async Task Admits_exactly_one_of_twenty_concurrent_inserts_of_one_username()
    {
        // FR-1.8. This is the test that fails if a store ever answers uniqueness from a prior
        // lookup instead of from the insert itself.
        var attempts = Enumerable
            .Range(0, 20)
            .Select(_ => Task.Run(() => Repository.TryAddAsync(NewUser("contended"), CancellationToken.None)))
            .ToArray();

        var outcomes = await Task.WhenAll(attempts);

        Assert.Equal(1, outcomes.Count(succeeded => succeeded));
    }

    [Fact]
    public async Task Round_trips_a_username_at_the_maximum_permitted_length()
    {
        var longest = new string('a', UserNamePolicy.MaxLength);

        Assert.True(await Repository.TryAddAsync(NewUser(longest), CancellationToken.None));
        Assert.NotNull(await Repository.FindByUserNameAsync(longest, CancellationToken.None));
    }

    public void Dispose()
    {
        _services.Dispose();
        DisposeStore();
        GC.SuppressFinalize(this);
    }

    /// <summary>Hook for stores that leave something behind, such as a file.</summary>
    protected virtual void DisposeStore()
    {
    }
}
