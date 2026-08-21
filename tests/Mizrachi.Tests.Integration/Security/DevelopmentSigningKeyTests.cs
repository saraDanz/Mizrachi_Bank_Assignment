using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mizrachi.Infrastructure.Security;

namespace Mizrachi.Tests.Integration.Security;

/// <summary>
/// The Development-only fallback for an absent <c>Jwt:SigningKey</c> (NFR-1.5), and the promise
/// that it changes nothing anywhere else (NFR-1.4).
/// </summary>
/// <remarks>
/// A convenience that relaxes a security control earns its tests at the boundary, in both
/// directions: that it applies where it is meant to, that it does not apply where it is not, and
/// that the key it invents never reaches the log (NFR-2.6).
/// </remarks>
public sealed class DevelopmentSigningKeyTests
{
    private const string EphemeralMarker = "generated in memory";

    private static IReadOnlyDictionary<string, string?> WithoutSigningKey() =>
        new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "InMemory",
            ["Jwt:SigningKey"] = null
        };

    [Fact]
    public async Task Development_without_a_configured_key_starts_and_warns()
    {
        var recorder = new RecordingLoggerProvider();

        using var factory = new LoggingApiFactory(
            recorder,
            environment: "Development",
            settings: WithoutSigningKey());

        using var client = factory.CreateApiClient();

        // It started, and the whole flow works: the token this process issues is a token this
        // process accepts, which is the point of generating a key rather than failing.
        var userName = "devkey" + Guid.NewGuid().ToString("N")[..8];
        var (userId, token) = await client.RegisterAndSignInAsync(userName);

        using var deleted = await client.DeleteUserAsync(userId, token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var warnings = recorder.Written
            .Where(entry => entry.Contains(EphemeralMarker, StringComparison.Ordinal))
            .ToList();

        // Exactly one: the options are materialised more than once per process, through both
        // IOptions and IOptionsMonitor, and a warning repeated per cache miss is noise.
        var warning = Assert.Single(warnings);

        Assert.Contains("Jwt:SigningKey", warning, StringComparison.Ordinal);
        Assert.Contains("256-bit", warning, StringComparison.Ordinal);
        Assert.Contains("ephemeral", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet user-secrets set", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_generated_key_is_random_long_enough_and_never_written_to_the_log()
    {
        var recorder = new RecordingLoggerProvider();

        using var first = new LoggingApiFactory(
            recorder,
            environment: "Development",
            settings: WithoutSigningKey());

        using var client = first.CreateApiClient();
        using (await client.ValidateAsync("nobody-at-all"))
        {
        }

        var generated = SigningKeyOf(first);

        Assert.False(string.IsNullOrWhiteSpace(generated));
        Assert.True(Encoding.UTF8.GetByteCount(generated) >= JwtOptions.MinimumSigningKeyBytes);
        Assert.NotEqual(ApiFactory.SigningKey, generated);

        // The warning announces that a key exists. It must never announce what it is, and
        // neither must anything else the host wrote.
        Assert.NotEmpty(recorder.Written);
        Assert.DoesNotContain(
            recorder.Written,
            entry => entry.Contains(generated, StringComparison.Ordinal));

        // Ephemeral means ephemeral: a second host does not reproduce the first one's key.
        using var second = new ApiFactory(
            environment: "Development",
            settings: WithoutSigningKey());

        Assert.NotEqual(generated, SigningKeyOf(second));
    }

    [Fact]
    public async Task Development_with_a_configured_key_uses_it_and_does_not_warn()
    {
        var recorder = new RecordingLoggerProvider();

        // No settings override, so ApiFactory supplies its signing key as usual.
        using var factory = new LoggingApiFactory(recorder, environment: "Development");

        using var client = factory.CreateApiClient();
        using (await client.ValidateAsync("nobody-at-all"))
        {
        }

        Assert.Equal(ApiFactory.SigningKey, SigningKeyOf(factory));

        Assert.NotEmpty(recorder.Written);
        Assert.DoesNotContain(
            recorder.Written,
            entry => entry.Contains(EphemeralMarker, StringComparison.Ordinal));
    }

    [Fact]
    public void Production_without_a_configured_key_still_fails_at_startup()
    {
        using var factory = new ApiFactory(
            environment: "Production",
            settings: WithoutSigningKey());

        // Not a 500 on the first request, and not a generated key: the host never opens.
        var error = Assert.ThrowsAny<Exception>(() =>
        {
            using var client = factory.CreateApiClient();
        });

        var text = error.ToString();

        Assert.Contains(nameof(OptionsValidationException), text, StringComparison.Ordinal);
        Assert.Contains("Jwt:SigningKey", text, StringComparison.Ordinal);
        Assert.Contains("dotnet user-secrets set", text, StringComparison.Ordinal);
    }

    private static string SigningKeyOf(ApiFactory factory) =>
        factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value.SigningKey;
}
