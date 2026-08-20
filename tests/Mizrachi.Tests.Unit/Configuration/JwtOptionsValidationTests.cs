using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mizrachi.Infrastructure;

namespace Mizrachi.Tests.Unit.Configuration;

/// <summary>
/// A missing signing key must stop the host with a message a stranger can act on (NFR-1.4), and
/// that message must never carry the value it is complaining about (NFR-2.6).
/// </summary>
/// <remarks>
/// Validation is driven through <see cref="IStartupValidator"/>, which is what
/// <c>ValidateOnStart</c> registers, so these tests fail for the same reason and at the same
/// moment the API does — rather than at first use of the options.
/// </remarks>
public class JwtOptionsValidationTests
{
    private const string ValidKey = "a-test-only-signing-key-of-at-least-32-bytes";

    private static IStartupValidator BuildValidator(params (string Key, string? Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "InMemory",
            ["Jwt:Issuer"] = "tests",
            ["Jwt:Audience"] = "tests",
            ["Jwt:SigningKey"] = ValidKey
        };

        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider()
            .GetRequiredService<IStartupValidator>();
    }

    [Fact]
    public void Complete_configuration_starts()
    {
        var validator = BuildValidator();

        validator.Validate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_signing_key_stops_startup(string? signingKey)
    {
        var validator = BuildValidator(("Jwt:SigningKey", signingKey));

        var error = Assert.Throws<OptionsValidationException>(() => validator.Validate());

        var message = string.Join(Environment.NewLine, error.Failures);

        // The three things a developer needs: which key, how to set it, and how big it must be.
        Assert.Contains("Jwt:SigningKey", message);
        Assert.Contains("dotnet user-secrets set", message);
        Assert.Contains("--project src/Mizrachi.Api", message);
        Assert.Contains("32", message);
    }

    [Fact]
    public void Missing_signing_key_message_points_at_a_store_outside_the_repository()
    {
        var validator = BuildValidator(("Jwt:SigningKey", null));

        var error = Assert.Throws<OptionsValidationException>(() => validator.Validate());

        var message = string.Join(Environment.NewLine, error.Failures);

        // It must not suggest putting the key in a file that could be committed.
        Assert.DoesNotContain("appsettings", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Short_signing_key_stops_startup_without_echoing_the_value()
    {
        const string tooShort = "correct-horse-battery";

        var validator = BuildValidator(("Jwt:SigningKey", tooShort));

        var error = Assert.Throws<OptionsValidationException>(() => validator.Validate());

        var message = string.Join(Environment.NewLine, error.Failures);

        Assert.Contains(tooShort.Length.ToString(), message);
        Assert.Contains("minimum is 32", message);

        // The whole point: the diagnostic never leaks the secret it is rejecting.
        Assert.DoesNotContain(tooShort, message);
    }

    [Fact]
    public void Valid_signing_key_is_never_echoed_on_an_unrelated_failure()
    {
        var validator = BuildValidator(("Jwt:Issuer", null));

        var error = Assert.Throws<OptionsValidationException>(() => validator.Validate());

        var message = string.Join(Environment.NewLine, error.Failures);

        Assert.Contains("Jwt:Issuer", message);
        Assert.DoesNotContain(ValidKey, message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Lifetime_outside_the_permitted_range_stops_startup(int minutes)
    {
        var validator = BuildValidator(("Jwt:LifetimeMinutes", minutes.ToString()));

        var error = Assert.Throws<OptionsValidationException>(() => validator.Validate());

        Assert.Contains("Jwt:LifetimeMinutes", string.Join(Environment.NewLine, error.Failures));
    }
}
