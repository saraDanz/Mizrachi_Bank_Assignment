using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Mizrachi.Tests.Integration;

/// <summary>
/// Hosts the real API in memory, configured exactly as it would be in production apart from the
/// store it points at.
/// </summary>
public class ApiFactory : WebApplicationFactory<Api.Program>
{
    public const string SigningKey = "a-test-only-signing-key-of-at-least-32-bytes";

    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public ApiFactory(
        string environment = "Development",
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        _environment = environment;
        _settings = settings ?? new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "InMemory"
        };
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        // UseSetting, not ConfigureAppConfiguration. The latter is applied after the app has
        // built its own configuration, by which point AddInfrastructure has already read
        // Persistence:Provider and chosen a store - so an override there silently does nothing.
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "mizrachi-bank-api",
            ["Jwt:Audience"] = "mizrachi-bank-api",
            ["Jwt:LifetimeMinutes"] = "15",
            ["Jwt:SigningKey"] = SigningKey
        };

        foreach (var setting in _settings)
        {
            settings[setting.Key] = setting.Value;
        }

        foreach (var setting in settings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }
    }

    /// <summary>
    /// A client that does not follow redirects, so an HTTPS redirect is visible as a redirect
    /// rather than silently followed.
    /// </summary>
    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
