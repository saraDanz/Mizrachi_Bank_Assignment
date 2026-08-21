using System.Net;
using System.Reflection;
using System.Text.Json;
using Mizrachi.Api.Controllers;
using Mizrachi.Domain;
using Mizrachi.Infrastructure.Persistence;

namespace Mizrachi.Tests.Integration.Security;

/// <summary>
/// The two password-exposure guarantees: never stored in a recoverable form (NFR-2.1), and
/// never returned in any response (FR-1.4, FR-4.1).
/// </summary>
public sealed class PasswordExposureTests
{
    /// <summary>
    /// Distinctive enough that finding it anywhere it should not be is unambiguous.
    /// </summary>
    private const string Sentinel = "Zq7-sentinel-passphrase-9wX";

    // ---- Never returned in any response ----

    [Fact]
    public void The_user_entity_is_never_a_response_type()
    {
        // The entity carries UserPassword because the schema says so. That is exactly why it
        // must not cross the boundary: returning it is how a stored hash escapes (FR-1.4).
        Assert.DoesNotContain(typeof(User), DeclaredResponseTypes());

        Assert.Contains(typeof(User).GetProperties(), property => property.Name == "UserPassword");
    }

    [Fact]
    public void No_response_type_declares_a_credential_member()
    {
        var forbidden = new[] { "password", "hash", "salt", "secret", "credential" };

        var responseTypes = DeclaredResponseTypes()
            .Concat(typeof(UsersController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(action => action.ReturnType)
                .SelectMany(Unwrap))
            .Distinct();

        foreach (var type in responseTypes)
        {
            foreach (var member in DataMembers(type))
            {
                foreach (var word in forbidden)
                {
                    Assert.False(
                        member.Name.Contains(word, StringComparison.OrdinalIgnoreCase),
                        $"{type.Name}.{member.Name} looks like a credential member on a type reachable from an endpoint.");
                }
            }
        }
    }

    private static List<Type> DeclaredResponseTypes() =>
        typeof(UsersController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(action => action.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute>())
            .Select(attribute => attribute.Type)
            .Where(type => type != typeof(void))
            .Distinct()
            .ToList();

    /// <summary>
    /// Properties and fields only. Methods are excluded deliberately: every object inherits
    /// GetHashCode, whose name contains "hash", and matching on that would make the check fire
    /// on everything and mean nothing.
    /// </summary>
    private static IEnumerable<MemberInfo> DataMembers(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Cast<MemberInfo>()
            .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance));

    private static IEnumerable<Type> Unwrap(Type type)
    {
        // Response bodies are declared on ProducesResponseType rather than in the signature,
        // so walk those too - IActionResult tells us nothing on its own.
        yield return type;

        foreach (var argument in type.GetGenericArguments())
        {
            yield return argument;
        }
    }

    [Fact]
    public void No_declared_response_type_carries_a_password_or_hash()
    {
        var responseTypes = typeof(UsersController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(action => action.GetCustomAttributes<Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute>())
            .Select(attribute => attribute.Type)
            .Where(type => type != typeof(void))
            .Distinct()
            .ToList();

        Assert.NotEmpty(responseTypes);

        foreach (var type in responseTypes)
        {
            foreach (var member in DataMembers(type))
            {
                Assert.DoesNotContain("password", member.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("hash", member.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task No_endpoint_echoes_the_submitted_password_in_its_body_or_headers()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateApiClient();
        var userName = "sentinel" + Guid.NewGuid().ToString("N")[..8];

        var responses = new List<HttpResponseMessage>();

        responses.Add(await client.CreateUserAsync(userName, Sentinel));
        var userId = (await responses[0].ReadJsonAsync()).GetProperty("userId").GetGuid();

        responses.Add(await client.ValidateAsync(userName, Sentinel));
        var token = (await responses[1].ReadJsonAsync()).GetProperty("token").GetString()!;

        responses.Add(await client.CreateUserAsync(userName, Sentinel));          // 409
        responses.Add(await client.ValidateAsync(userName, "the-wrong-one-here")); // 401
        responses.Add(await client.DeleteUserAsync(Guid.NewGuid(), token));        // 403
        responses.Add(await client.DeleteUserAsync(userId, token));                // 204

        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain(Sentinel, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Sentinel, response.Headers.ToString(), StringComparison.OrdinalIgnoreCase);

            response.Dispose();
        }
    }

    // ---- Never stored in a recoverable form ----

    [Fact]
    public async Task The_sqlite_file_contains_no_trace_of_the_plaintext_password()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "mizrachi-tests", $"{Guid.NewGuid():N}.db");

        try
        {
            using (var factory = new ApiFactory(settings: new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = PersistenceOptions.Providers.Sqlite,
                ["Persistence:FilePath"] = databasePath
            }))
            {
                using var client = factory.CreateApiClient();
                using var created = await client.CreateUserAsync("sentineluser", Sentinel);
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            }

            AssertNoSentinelInFile(databasePath);
            AssertNoSentinelInFile(databasePath + "-wal", required: false);
        }
        finally
        {
            Cleanup(databasePath, string.Empty, "-wal", "-shm");
        }
    }

    [Fact]
    public async Task The_json_file_contains_no_trace_of_the_plaintext_password()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "mizrachi-tests", $"{Guid.NewGuid():N}.json");

        try
        {
            using (var factory = new ApiFactory(settings: new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = PersistenceOptions.Providers.JsonFile,
                ["Persistence:FilePath"] = filePath
            }))
            {
                using var client = factory.CreateApiClient();
                using var created = await client.CreateUserAsync("sentineluser", Sentinel);
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            }

            AssertNoSentinelInFile(filePath);

            // And what is stored is a hash, not an encoding of the password.
            var stored = JsonDocument.Parse(ReadAllTextShared(filePath))
                .RootElement[0]
                .GetProperty("UserPassword")
                .GetString()!;

            Assert.NotEqual(Sentinel, stored);
            Assert.DoesNotContain(Sentinel, stored, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Sentinel)),
                stored,
                StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(filePath, string.Empty, ".tmp");
        }
    }

    [Fact]
    public async Task Two_accounts_sharing_a_password_store_different_values()
    {
        // NFR-2.1 requires a per-user salt. Without one, identical passwords produce identical
        // stored values and a single precomputed table breaks every account that shares one.
        var filePath = Path.Combine(Path.GetTempPath(), "mizrachi-tests", $"{Guid.NewGuid():N}.json");

        try
        {
            using (var factory = new ApiFactory(settings: new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = PersistenceOptions.Providers.JsonFile,
                ["Persistence:FilePath"] = filePath
            }))
            {
                using var client = factory.CreateApiClient();
                await client.CreateUserAsync("firstuser", Sentinel);
                await client.CreateUserAsync("seconduser", Sentinel);
            }

            var document = JsonDocument.Parse(ReadAllTextShared(filePath)).RootElement;

            Assert.Equal(2, document.GetArrayLength());
            Assert.NotEqual(
                document[0].GetProperty("UserPassword").GetString(),
                document[1].GetProperty("UserPassword").GetString());
        }
        finally
        {
            Cleanup(filePath, string.Empty, ".tmp");
        }
    }

    /// <param name="required">
    /// When true the file must exist. A scan that silently skips a missing file is a test that
    /// cannot fail — it would report success for a store that never wrote anything.
    /// </param>
    private static void AssertNoSentinelInFile(string path, bool required = true)
    {
        if (!File.Exists(path))
        {
            Assert.False(required, $"Expected the store to have written {Path.GetFileName(path)}, but it does not exist.");
            return;
        }

        // A permissive share mode: SQLite's connection pool can still hold the file open, and
        // failing to read it would look like the scan passing.
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var buffer = new MemoryStream())
        {
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }

        foreach (var encoding in new[] { System.Text.Encoding.UTF8, System.Text.Encoding.Unicode })
        {
            var needle = encoding.GetBytes(Sentinel);
            Assert.False(
                Contains(bytes, needle),
                $"The plaintext password appears in {Path.GetFileName(path)} as {encoding.EncodingName}.");
        }
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var start = 0; start + needle.Length <= haystack.Length; start++)
        {
            var matched = true;

            for (var offset = 0; offset < needle.Length; offset++)
            {
                if (haystack[start + offset] != needle[offset])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Cleanup(string basePath, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            try
            {
                File.Delete(basePath + suffix);
            }
            catch (IOException)
            {
                // Throwaway temp file.
            }
        }
    }
}
