using System.Text;
using Microsoft.Extensions.Options;

namespace Mizrachi.Infrastructure.Security;

/// <summary>
/// Startup validation for <see cref="JwtOptions"/> (NFR-1.4).
/// </summary>
/// <remarks>
/// This replaces data-annotation validation, which reports only that a member is required. A
/// developer who has never opened this repository should be able to act on the failure without
/// reading any source, so each message names the configuration key and the command that sets it.
///
/// No message contains the configured value or any part of it. A startup failure is written to
/// the console and to every attached log sink, and a signing key must reach neither (NFR-2.6).
/// The key's length is reported because that is what makes a too-short key actionable; a length
/// is not a secret and does not narrow a search for the value.
/// </remarks>
internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private const string UserSecretsCommand =
        "dotnet user-secrets set \"Jwt:SigningKey\" \"<value>\" --project src/Mizrachi.Api";

    private const string EnvironmentCommand =
        "$env:Jwt__SigningKey = \"<value>\"        (PowerShell, this session only)";

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Jwt:Issuer is not set. It is expected in appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Jwt:Audience is not set. It is expected in appsettings.json.");
        }

        if (options.LifetimeMinutes is < 1 or > 60)
        {
            failures.Add(
                $"Jwt:LifetimeMinutes is {options.LifetimeMinutes}, which is outside the " +
                "permitted range of 1 to 60.");
        }

        ValidateSigningKey(options.SigningKey, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateSigningKey(string signingKey, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            failures.Add(string.Join(Environment.NewLine, new[]
            {
                "Jwt:SigningKey is not set. It has no default, because a signing key must never",
                "be committed to the repository. Set it once, stored outside the working tree:",
                string.Empty,
                "    " + UserSecretsCommand,
                string.Empty,
                "or, without persisting it:",
                string.Empty,
                "    " + EnvironmentCommand,
                string.Empty,
                $"Use at least {JwtOptions.MinimumSigningKeyBytes} bytes of random data. The " +
                    "README section 'Running it' has a one-liner that generates one."
            }));

            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(signingKey);
        if (byteCount < JwtOptions.MinimumSigningKeyBytes)
        {
            failures.Add(string.Join(Environment.NewLine, new[]
            {
                $"Jwt:SigningKey is {byteCount} bytes; the minimum is " +
                    $"{JwtOptions.MinimumSigningKeyBytes}.",
                "An HMAC-SHA256 key shorter than the hash it feeds weakens every signature it",
                "produces. Replace it with a longer random value using the same command:",
                string.Empty,
                "    " + UserSecretsCommand,
                string.Empty,
                "The configured value is not shown here, by design."
            }));
        }
    }
}
