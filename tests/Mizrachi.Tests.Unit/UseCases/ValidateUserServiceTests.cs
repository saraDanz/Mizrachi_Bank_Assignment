using Mizrachi.Application.UseCases;
using Mizrachi.Domain;
using Mizrachi.Tests.Unit.Fakes;

namespace Mizrachi.Tests.Unit.UseCases;

public sealed class ValidateUserServiceTests
{
    private const string KnownPassword = "a-long-enough-passphrase";

    private readonly FakeUserRepository _repository = new();
    private readonly CountingPasswordHasher _hasher = new();
    private readonly FakeTokenIssuer _tokenIssuer = new();
    private readonly RecordingSecurityEventLog _events = new();
    private readonly Guid _knownUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private ValidateUserService CreateService()
    {
        // Constructing the service computes the absent-user hash, so counters are reset after.
        var service = new ValidateUserService(_repository, _hasher, _tokenIssuer, _events);
        _repository.Seed(User.Create(_knownUserId, "alice", _hasher.Hash(KnownPassword)));
        ResetHasherCounters();
        return service;
    }

    private void ResetHasherCounters()
    {
        // The hasher counts across its lifetime; tests assert on calls made by ExecuteAsync,
        // so a fresh hasher is swapped in via a fresh field only where needed. Instead of
        // mutating state, tests below record the baseline explicitly.
        _verifyBaseline = _hasher.VerifyCount;
        _hashBaseline = _hasher.HashCount;
    }

    private int _verifyBaseline;
    private int _hashBaseline;

    private int VerifiesDuringExecute => _hasher.VerifyCount - _verifyBaseline;

    private int HashesDuringExecute => _hasher.HashCount - _hashBaseline;

    [Fact]
    public async Task Authenticates_a_correct_username_and_password()
    {
        var result = await CreateService().ExecuteAsync("alice", KnownPassword, CancellationToken.None);

        var authenticated = Assert.IsType<ValidateUserResult.Authenticated>(result);
        Assert.Equal(_knownUserId, authenticated.UserId);
        Assert.Equal("alice", authenticated.UserName);
        Assert.False(string.IsNullOrWhiteSpace(authenticated.Token.Token));
        Assert.Equal(new[] { _knownUserId }, _events.AuthenticationSuccesses);
    }

    [Fact]
    public async Task Verifies_a_hash_even_when_the_username_is_unknown()
    {
        // FR-3.6: the work performed must not depend on whether the account was found.
        var result = await CreateService().ExecuteAsync("nobody", KnownPassword, CancellationToken.None);

        Assert.IsType<ValidateUserResult.Rejected>(result);
        Assert.Equal(1, VerifiesDuringExecute);
    }

    [Fact]
    public async Task Performs_the_same_number_of_verifications_for_both_kinds_of_failure()
    {
        var service = CreateService();

        await service.ExecuteAsync("nobody", KnownPassword, CancellationToken.None);
        var afterUnknownUser = VerifiesDuringExecute;

        await service.ExecuteAsync("alice", "the-wrong-passphrase", CancellationToken.None);
        var afterWrongPassword = VerifiesDuringExecute - afterUnknownUser;

        Assert.Equal(afterUnknownUser, afterWrongPassword);
    }

    [Fact]
    public async Task Looks_the_user_up_even_when_the_username_is_unknown()
    {
        var service = CreateService();
        var before = _repository.FindByUserNameCalls;

        await service.ExecuteAsync("nobody", KnownPassword, CancellationToken.None);

        Assert.Equal(before + 1, _repository.FindByUserNameCalls);
    }

    [Fact]
    public async Task Returns_the_same_result_type_for_an_unknown_user_and_a_wrong_password()
    {
        // FR-3.5: there is one failure case, so the two are indistinguishable by construction.
        var service = CreateService();

        var unknownUser = await service.ExecuteAsync("nobody", KnownPassword, CancellationToken.None);
        var wrongPassword = await service.ExecuteAsync("alice", "the-wrong-passphrase", CancellationToken.None);

        Assert.IsType<ValidateUserResult.Rejected>(unknownUser);
        Assert.IsType<ValidateUserResult.Rejected>(wrongPassword);
        Assert.Equal(unknownUser, wrongPassword);
    }

    [Fact]
    public async Task Records_a_failure_without_any_username()
    {
        var service = CreateService();

        await service.ExecuteAsync("alice", "the-wrong-passphrase", CancellationToken.None);

        // NFR-2.3: there is no overload that could carry the submitted username.
        Assert.Equal(1, _events.AuthenticationFailures);
        Assert.Empty(_events.AuthenticationSuccesses);
    }

    [Fact]
    public async Task Issues_no_token_on_failure()
    {
        var service = CreateService();

        await service.ExecuteAsync("alice", "the-wrong-passphrase", CancellationToken.None);

        Assert.Equal(0, _tokenIssuer.IssueCount);
    }

    [Fact]
    public async Task Matches_a_username_ignoring_case_and_surrounding_whitespace()
    {
        var result = await CreateService().ExecuteAsync("  ALICE  ", KnownPassword, CancellationToken.None);

        Assert.IsType<ValidateUserResult.Authenticated>(result);
    }

    [Fact]
    public async Task Rejects_an_over_length_password_without_hashing_it()
    {
        var service = CreateService();

        var result = await service.ExecuteAsync(
            "alice",
            new string('a', PasswordPolicy.MaxLength + 1),
            CancellationToken.None);

        Assert.IsType<ValidateUserResult.Rejected>(result);
        Assert.Equal(0, VerifiesDuringExecute);
        Assert.Equal(0, HashesDuringExecute);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Rejects_a_missing_password_without_authenticating(string? password)
    {
        var result = await CreateService().ExecuteAsync("alice", password, CancellationToken.None);

        Assert.IsType<ValidateUserResult.Rejected>(result);
    }
}
