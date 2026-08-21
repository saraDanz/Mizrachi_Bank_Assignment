using System.Net;

namespace Mizrachi.Tests.Integration.Security;

/// <summary>
/// What the API must not expose: documentation and internals outside Development (NFR-2.7,
/// FR-4.3), and credentials in the log (NFR-2.3).
/// </summary>
public sealed class EnvironmentAndLoggingTests
{
    private const string Sentinel = "Zq7-sentinel-passphrase-9wX";

    [Fact]
    public async Task Swagger_is_absent_outside_development()
    {
        using var factory = new ApiFactory(environment: "Production");
        using var client = factory.CreateApiClient();

        using var swagger = await client.GetAsync("/swagger/index.html");
        using var document = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.NotFound, swagger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);
    }

    [Fact]
    public async Task Swagger_is_present_in_development()
    {
        // The negative above is only meaningful if the positive holds.
        using var factory = new ApiFactory(environment: "Development");
        using var client = factory.CreateApiClient();

        using var document = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
    }

    [Fact]
    public async Task No_log_entry_contains_a_password_or_a_token()
    {
        var recorder = new RecordingLoggerProvider();

        using var factory = new LoggingApiFactory(recorder);
        using var client = factory.CreateApiClient();

        var userName = "loguser" + Guid.NewGuid().ToString("N")[..8];

        using (await client.CreateUserAsync(userName, Sentinel))
        using (var validated = await client.ValidateAsync(userName, Sentinel))
        using (await client.ValidateAsync(userName, "the-wrong-passphrase"))
        using (await client.ValidateAsync("nobody-at-all", Sentinel))
        {
            var token = (await validated.ReadJsonAsync()).GetProperty("token").GetString()!;

            var written = recorder.Written;

            Assert.NotEmpty(written);
            Assert.DoesNotContain(written, entry => entry.Contains(Sentinel, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(written, entry => entry.Contains(token, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task No_log_entry_names_the_username_on_a_failed_authentication()
    {
        // NFR-2.3: a failed attempt's username may be a mistyped near-miss credential.
        var recorder = new RecordingLoggerProvider();

        using var factory = new LoggingApiFactory(recorder);
        using var client = factory.CreateApiClient();

        var mistyped = "almostright" + Guid.NewGuid().ToString("N")[..8];

        using (await client.ValidateAsync(mistyped, Sentinel))
        {
            Assert.DoesNotContain(
                recorder.Written,
                entry => entry.Contains(mistyped, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task An_unhandled_error_outside_development_reveals_no_internals()
    {
        // Forcing a failure: an unparseable body reaches the pipeline before any handler.
        using var factory = new ApiFactory(environment: "Production");
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsync(
            "/api/users",
            new StringContent("{ not json at all", System.Text.Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();

        Assert.True((int)response.StatusCode >= 400);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Mizrachi.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Sqlite", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_response_carries_a_correlation_id_that_the_caller_can_quote()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.ValidateAsync("nobody", "a-long-enough-passphrase");

        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var header));
        Assert.Equal(
            header!.Single(),
            (await response.ReadJsonAsync()).GetProperty("correlationId").GetString());
    }
}
