using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mizrachi.Tests.Integration;

/// <summary>Small helpers so the tests read as scenarios rather than as HTTP plumbing.</summary>
internal static class ApiClientExtensions
{
    internal const string ValidPassword = "a-long-enough-passphrase";

    internal static Task<HttpResponseMessage> CreateUserAsync(
        this HttpClient client,
        string userName,
        string password = ValidPassword) =>
        client.PostAsJsonAsync("/api/users", new { userName, password });

    internal static Task<HttpResponseMessage> ValidateAsync(
        this HttpClient client,
        string userName,
        string password = ValidPassword) =>
        client.PostAsJsonAsync("/api/users/validate", new { userName, password });

    internal static async Task<HttpResponseMessage> DeleteUserAsync(
        this HttpClient client,
        Guid userId,
        string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/users/{userId}");

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Registers a user and validates, returning the identifier and its token.</summary>
    internal static async Task<(Guid UserId, string Token)> RegisterAndSignInAsync(
        this HttpClient client,
        string userName,
        string password = ValidPassword)
    {
        using var created = await client.CreateUserAsync(userName, password);
        created.EnsureSuccessStatusCode();

        using var validated = await client.ValidateAsync(userName, password);
        validated.EnsureSuccessStatusCode();

        var body = await validated.Content.ReadFromJsonAsync<JsonElement>();

        return (body.GetProperty("userId").GetGuid(), body.GetProperty("token").GetString()!);
    }

    internal static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
