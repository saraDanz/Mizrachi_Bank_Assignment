using System.Text.Json;

namespace Mizrachi.Tests.Integration.Security;

/// <summary>
/// The published document must describe authorization the way the API enforces it (FR-1.10,
/// FR-2.2, FR-3.7), and must never carry a token of its own (NFR-2.6).
/// </summary>
/// <remarks>
/// The failure this guards against is a global <c>AddSecurityRequirement</c>, which is the
/// obvious way to make the Authorize button work and quietly puts a padlock on the two endpoints
/// that are anonymous by requirement. A reviewer then believes registration needs a token it
/// does not need, and the document contradicts the API at the first call they try.
/// </remarks>
public sealed class OpenApiSecurityTests
{
    private static async Task<JsonDocument> DocumentAsync()
    {
        using var factory = new ApiFactory(environment: "Development");
        using var client = factory.CreateApiClient();

        return JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
    }

    private static bool RequiresAuthorization(JsonDocument document, string path, string verb) =>
        document.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(verb)
            .TryGetProperty("security", out var security)
        && security.GetArrayLength() > 0;

    [Fact]
    public async Task Marks_delete_as_requiring_a_bearer_token()
    {
        using var document = await DocumentAsync();

        Assert.True(RequiresAuthorization(document, "/api/users/{userId}", "delete"));
    }

    [Theory]
    [InlineData("/api/users", "post")]
    [InlineData("/api/users/validate", "post")]
    public async Task Leaves_the_anonymous_endpoints_unmarked(string path, string verb)
    {
        using var document = await DocumentAsync();

        Assert.False(RequiresAuthorization(document, path, verb));
    }

    [Fact]
    public async Task Declares_no_document_wide_security_requirement()
    {
        using var document = await DocumentAsync();

        // A root-level "security" node applies to every operation, which is exactly the
        // blanket behaviour the operation filter exists to avoid.
        Assert.False(document.RootElement.TryGetProperty("security", out _));
    }

    [Fact]
    public async Task Defines_a_bearer_scheme_carrying_no_value()
    {
        using var document = await DocumentAsync();

        var scheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());

        // Nothing token-shaped may be published with the document.
        foreach (var slot in new[] { "default", "example", "value" })
        {
            Assert.False(scheme.TryGetProperty(slot, out _));
        }
    }

    [Fact]
    public async Task Publishes_no_token_anywhere_in_the_document()
    {
        using var factory = new ApiFactory(environment: "Development");
        using var client = factory.CreateApiClient();

        var raw = await client.GetStringAsync("/swagger/v1/swagger.json");

        // A JWT's header segment is a stable prefix; no serialised token may appear at all.
        Assert.DoesNotContain("eyJ", raw, StringComparison.Ordinal);
    }
}
