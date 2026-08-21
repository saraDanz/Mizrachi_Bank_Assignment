using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mizrachi.Tests.Integration.Security;

/// <summary>
/// The endpoint must not become an oracle: neither for which accounts exist (FR-3.5), nor for
/// which identifiers are real (FR-2.4).
/// </summary>
public sealed class EnumerationAndAuthorizationTests : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory();
        _client = _factory.CreateApiClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static string UniqueName() => "user" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Strips the fields that legitimately vary per request. The correlation id is
    /// request-scoped and not derived from account state; <c>instance</c> echoes the path the
    /// caller already chose.
    /// </summary>
    private static async Task<string> ComparableBody(HttpResponseMessage response)
    {
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();

        node.Remove("correlationId");
        node.Remove("instance");

        return node.ToJsonString();
    }

    [Fact]
    public async Task An_unknown_username_and_a_wrong_password_are_answered_identically()
    {
        var userName = UniqueName();
        await _client.CreateUserAsync(userName);

        using var unknownUser = await _client.ValidateAsync(UniqueName());
        using var wrongPassword = await _client.ValidateAsync(userName, "the-wrong-passphrase");

        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(await ComparableBody(unknownUser), await ComparableBody(wrongPassword));
    }

    [Fact]
    public async Task The_rejection_body_is_a_fixed_value_that_describes_no_account()
    {
        // Comparing the two failures to each other is not enough on its own: a detail string
        // that leaked account state would still match itself. FR-3.5 asks for a fixed body, so
        // the body is pinned, and separately checked not to echo what the caller submitted.
        var userName = UniqueName();
        await _client.CreateUserAsync(userName);

        using var response = await _client.ValidateAsync(userName, "the-wrong-passphrase");

        var body = await response.ReadJsonAsync();

        Assert.Equal("Unauthorized", body.GetProperty("title").GetString());
        Assert.Equal("Invalid credentials.", body.GetProperty("detail").GetString());

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(userName, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exist", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_deleted_account_is_answered_like_one_that_never_existed()
    {
        var userName = UniqueName();
        var (userId, token) = await _client.RegisterAndSignInAsync(userName);
        await _client.DeleteUserAsync(userId, token);

        using var deletedAccount = await _client.ValidateAsync(userName);
        using var neverExisted = await _client.ValidateAsync(UniqueName());

        Assert.Equal(await ComparableBody(neverExisted), await ComparableBody(deletedAccount));
    }

    [Fact]
    public async Task Deleting_another_users_account_is_forbidden_and_leaves_it_intact()
    {
        var victimName = UniqueName();
        var (victimId, _) = await _client.RegisterAndSignInAsync(victimName);
        var (_, attackerToken) = await _client.RegisterAndSignInAsync(UniqueName());

        using var attempt = await _client.DeleteUserAsync(victimId, attackerToken);

        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);

        using var victimStillWorks = await _client.ValidateAsync(victimName);
        Assert.Equal(HttpStatusCode.OK, victimStillWorks.StatusCode);
    }

    [Fact]
    public async Task A_real_unowned_id_and_an_id_that_was_never_issued_are_refused_identically()
    {
        // FR-2.4. This is the test that catches an implementation which checks existence first
        // and lets a 404 slip out for identifiers that do not exist.
        var (victimId, _) = await _client.RegisterAndSignInAsync(UniqueName());
        var (_, attackerToken) = await _client.RegisterAndSignInAsync(UniqueName());

        using var realButUnowned = await _client.DeleteUserAsync(victimId, attackerToken);
        using var neverIssued = await _client.DeleteUserAsync(Guid.NewGuid(), attackerToken);

        Assert.Equal(realButUnowned.StatusCode, neverIssued.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, realButUnowned.StatusCode);
        Assert.Equal(await ComparableBody(realButUnowned), await ComparableBody(neverIssued));
    }

    [Fact]
    public async Task An_unauthenticated_delete_is_rejected()
    {
        var (userId, _) = await _client.RegisterAndSignInAsync(UniqueName());

        using var response = await _client.DeleteUserAsync(userId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var stillThere = await _client.ValidateAsync(UniqueName());
        Assert.Equal(HttpStatusCode.Unauthorized, stillThere.StatusCode);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_rejected()
    {
        var (userId, _) = await _client.RegisterAndSignInAsync(UniqueName());

        var forged = ForgeToken(userId, "a-completely-different-signing-key-32b");

        using var response = await _client.DeleteUserAsync(userId, forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_with_no_signature_is_rejected()
    {
        // The alg:none attack. ValidAlgorithms is pinned, so this must not be honoured.
        var (userId, _) = await _client.RegisterAndSignInAsync(UniqueName());

        var header = Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64Url($"{{\"sub\":\"{userId}\",\"iss\":\"mizrachi-bank-api\",\"aud\":\"mizrachi-bank-api\"}}");

        using var response = await _client.DeleteUserAsync(userId, $"{header}.{payload}.");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string ForgeToken(Guid subject, string signingKey)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "mizrachi-bank-api",
            audience: "mizrachi-bank-api",
            claims: new[] { new System.Security.Claims.Claim("sub", subject.ToString()) },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(signingKey)),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));

        return handler.WriteToken(token);
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    [Fact]
    public async Task Credentials_are_not_accepted_outside_the_request_body()
    {
        // FR-3.2: there must be no GET form of validation that would put a password in a URL,
        // where it lands in access logs, proxies and browser history.
        using var viaQueryString = await _client.GetAsync(
            "/api/users/validate?userName=alice&password=a-long-enough-passphrase");

        Assert.NotEqual(HttpStatusCode.OK, viaQueryString.StatusCode);
    }

    [Fact]
    public async Task A_client_supplied_user_id_is_ignored()
    {
        // FR-1.2: over-posting must not let a caller choose its own identifier.
        var chosen = Guid.NewGuid();

        using var response = await _client.PostAsJsonAsync("/api/users", new
        {
            userId = chosen,
            userName = UniqueName(),
            password = ApiClientExtensions.ValidPassword
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEqual(chosen, (await response.ReadJsonAsync()).GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task Concurrent_registrations_of_one_username_admit_exactly_one()
    {
        // FR-1.8, over HTTP rather than at the repository.
        var userName = UniqueName();

        // Five, not more: the registration rate limit is 5/minute per address, and a 429 would
        // mask whether the store refused the duplicate. Deeper concurrency is covered at the
        // repository level, where the contract suite fires 20 at once.
        var attempts = Enumerable
            .Range(0, 5)
            .Select(_ => _client.CreateUserAsync(userName))
            .ToArray();

        var responses = await Task.WhenAll(attempts);

        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.All(
                responses.Where(response => response.StatusCode != HttpStatusCode.Created),
                response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }
}
