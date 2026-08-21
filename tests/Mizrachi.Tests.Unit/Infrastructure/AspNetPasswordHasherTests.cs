using Mizrachi.Application.Abstractions;
using Mizrachi.Infrastructure.Security;

namespace Mizrachi.Tests.Unit.Infrastructure;

public sealed class AspNetPasswordHasherTests
{
    private const string Password = "a-long-enough-passphrase";

    private readonly AspNetPasswordHasher _hasher = new();

    [Fact]
    public void Verifies_a_password_it_hashed()
    {
        var hash = _hasher.Hash(Password);

        Assert.Equal(PasswordVerification.Success, _hasher.Verify(hash, Password));
    }

    [Fact]
    public void Rejects_a_wrong_password()
    {
        var hash = _hasher.Hash(Password);

        Assert.Equal(PasswordVerification.Failed, _hasher.Verify(hash, "a-different-passphrase"));
    }

    [Fact]
    public void Never_stores_the_plaintext_password_in_the_hash()
    {
        // NFR-2.1: passwords are never stored in a recoverable form.
        var hash = _hasher.Hash(Password);

        Assert.DoesNotContain(Password, hash, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passphrase", hash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Uses_a_fresh_salt_for_every_hash()
    {
        // NFR-2.1 requires a per-user salt: the same password must not produce the same stored
        // value twice, or one precomputed table would break every account that shares it.
        var first = _hasher.Hash(Password);
        var second = _hasher.Hash(Password);

        Assert.NotEqual(first, second);
        Assert.Equal(PasswordVerification.Success, _hasher.Verify(first, Password));
        Assert.Equal(PasswordVerification.Success, _hasher.Verify(second, Password));
    }

    [Fact]
    public void Rejects_a_malformed_stored_hash_rather_than_throwing()
    {
        Assert.Equal(PasswordVerification.Failed, _hasher.Verify("not-a-hash", Password));
    }

    [Fact]
    public void Distinguishes_passwords_differing_only_past_the_seventy_second_byte()
    {
        // Why not bcrypt: it truncates at 72 bytes, so these two would verify against each
        // other. FR-5.2 allows 128 characters and FR-5.3 allows any of them.
        var prefix = new string('a', 72);
        var hash = _hasher.Hash(prefix + "one");

        Assert.Equal(PasswordVerification.Failed, _hasher.Verify(hash, prefix + "two"));
    }

    [Fact]
    public void Applies_the_configured_iteration_count()
    {
        Assert.Equal(210_000, AspNetPasswordHasher.IterationCount);
    }
}
