using System.Net;
using Mizrachi.Infrastructure.Persistence;

namespace Mizrachi.Tests.Integration;

/// <summary>
/// The lifecycle over real HTTP: register, validate, delete, and the states either side.
/// Runs against every provider, because the API's behaviour must not depend on its store.
/// </summary>
public abstract class UserLifecycleTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    protected abstract IReadOnlyDictionary<string, string?> ProviderSettings();

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(settings: ProviderSettings());
        _client = _factory.CreateApiClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        CleanUpStore();
        return Task.CompletedTask;
    }

    protected virtual void CleanUpStore()
    {
    }

    private static string UniqueName() => "user" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task Registers_validates_and_deletes_an_account()
    {
        var userName = UniqueName();

        using var created = await _client.CreateUserAsync(userName);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body = await created.ReadJsonAsync();
        var userId = body.GetProperty("userId").GetGuid();
        Assert.Equal(userName, body.GetProperty("userName").GetString());
        Assert.Equal($"/api/users/{userId}", created.Headers.Location?.ToString());

        using var validated = await _client.ValidateAsync(userName);
        Assert.Equal(HttpStatusCode.OK, validated.StatusCode);
        var token = (await validated.ReadJsonAsync()).GetProperty("token").GetString()!;

        using var deleted = await _client.DeleteUserAsync(userId, token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var afterDelete = await _client.ValidateAsync(userName);
        Assert.Equal(HttpStatusCode.Unauthorized, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Refuses_a_duplicate_username_as_a_conflict()
    {
        var userName = UniqueName();
        using var first = await _client.CreateUserAsync(userName);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await _client.CreateUserAsync(userName.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Frees_the_username_after_the_account_is_deleted()
    {
        var userName = UniqueName();
        var (userId, token) = await _client.RegisterAndSignInAsync(userName);
        await _client.DeleteUserAsync(userId, token);

        using var again = await _client.CreateUserAsync(userName);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task Reports_not_found_when_deleting_an_already_deleted_own_account()
    {
        var (userId, token) = await _client.RegisterAndSignInAsync(UniqueName());
        await _client.DeleteUserAsync(userId, token);

        using var repeat = await _client.DeleteUserAsync(userId, token);

        Assert.Equal(HttpStatusCode.NotFound, repeat.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_password_that_fails_the_policy_and_names_the_rule()
    {
        using var response = await _client.CreateUserAsync(UniqueName(), "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("password_too_short", (await response.ReadJsonAsync()).GetProperty("rule").GetString());
    }

    [Fact]
    public async Task Accepts_a_username_with_surrounding_whitespace_and_stores_it_trimmed()
    {
        var userName = UniqueName();

        using var created = await _client.CreateUserAsync($"  {userName}  ");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(userName, (await created.ReadJsonAsync()).GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Carries_a_correlation_id_on_every_response()
    {
        using var response = await _client.CreateUserAsync(UniqueName());

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }
}

public sealed class InMemoryLifecycleTests : UserLifecycleTests
{
    protected override IReadOnlyDictionary<string, string?> ProviderSettings() =>
        new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = PersistenceOptions.Providers.InMemory
        };
}

public sealed class SqliteLifecycleTests : UserLifecycleTests
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), "mizrachi-tests", $"{Guid.NewGuid():N}.db");

    protected override IReadOnlyDictionary<string, string?> ProviderSettings() =>
        new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = PersistenceOptions.Providers.Sqlite,
            ["Persistence:FilePath"] = _databasePath
        };

    protected override void CleanUpStore()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(_databasePath + suffix);
            }
            catch (IOException)
            {
                // Throwaway temp file.
            }
        }
    }
}
