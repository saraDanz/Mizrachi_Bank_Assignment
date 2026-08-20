using Mizrachi.Application.UseCases;
using Mizrachi.Domain;
using Mizrachi.Tests.Unit.Fakes;

namespace Mizrachi.Tests.Unit.UseCases;

public sealed class CreateUserServiceTests
{
    private const string ValidPassword = "a-long-enough-passphrase";

    private readonly FakeUserRepository _repository = new();
    private readonly CountingPasswordHasher _hasher = new();
    private readonly RecordingSecurityEventLog _events = new();
    private readonly Guid _generatedId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private CreateUserService CreateService(params string[] deniedPasswords) =>
        new(_repository,
            _hasher,
            new PasswordPolicy(new StubPasswordDenyList(deniedPasswords)),
            new UserNamePolicy(),
            new FakeIdGenerator(_generatedId),
            _events);

    [Fact]
    public async Task Creates_a_user_with_a_server_generated_id()
    {
        var result = await CreateService().ExecuteAsync("alice", ValidPassword, CancellationToken.None);

        var created = Assert.IsType<CreateUserResult.Created>(result);
        Assert.Equal(_generatedId, created.UserId);
        Assert.Equal("alice", created.UserName);
        Assert.Equal(new[] { _generatedId }, _events.UsersCreated);
    }

    [Fact]
    public async Task Stores_a_hash_and_never_the_plaintext_password()
    {
        await CreateService().ExecuteAsync("alice", ValidPassword, CancellationToken.None);

        var stored = await _repository.FindByUserNameAsync("alice", CancellationToken.None);

        Assert.NotNull(stored);
        Assert.NotEqual(ValidPassword, stored!.UserPassword);
        Assert.DoesNotContain(ValidPassword, stored.UserPassword, StringComparison.Ordinal);
        Assert.Equal(1, _hasher.HashCount);
    }

    [Fact]
    public async Task Trims_the_username_before_storing_it()
    {
        var result = await CreateService().ExecuteAsync("   alice   ", ValidPassword, CancellationToken.None);

        var created = Assert.IsType<CreateUserResult.Created>(result);
        Assert.Equal("alice", created.UserName);
    }

    [Fact]
    public async Task Reports_a_duplicate_when_the_datastore_refuses_the_insert()
    {
        var service = CreateService();
        await service.ExecuteAsync("alice", ValidPassword, CancellationToken.None);

        var result = await service.ExecuteAsync("alice", "another-long-passphrase", CancellationToken.None);

        Assert.IsType<CreateUserResult.DuplicateUserName>(result);
    }

    [Fact]
    public async Task Reports_a_duplicate_when_it_loses_a_concurrent_race()
    {
        // FR-1.8: the loser of the race learns it lost from TryAddAsync, not from a prior check.
        _repository.NextAddLosesTheRace = true;

        var result = await CreateService().ExecuteAsync("alice", ValidPassword, CancellationToken.None);

        Assert.IsType<CreateUserResult.DuplicateUserName>(result);
        Assert.Empty(_events.UsersCreated);
    }

    [Fact]
    public async Task Rejects_an_invalid_username_without_hashing_anything()
    {
        var result = await CreateService().ExecuteAsync("a", ValidPassword, CancellationToken.None);

        var invalid = Assert.IsType<CreateUserResult.InvalidUserName>(result);
        Assert.Equal(UserNamePolicy.Rules.TooShort, invalid.Rule);
        Assert.Equal(0, _hasher.HashCount);
        Assert.Equal(0, _repository.TryAddCalls);
    }

    [Fact]
    public async Task Rejects_an_over_length_password_before_hashing_it()
    {
        // FR-5.2: the bound exists to cap the cost of hashing, so it must precede the hasher.
        var result = await CreateService().ExecuteAsync(
            "alice",
            new string('a', PasswordPolicy.MaxLength + 1),
            CancellationToken.None);

        var invalid = Assert.IsType<CreateUserResult.InvalidPassword>(result);
        Assert.Equal(PasswordPolicy.Rules.TooLong, invalid.Rule);
        Assert.Equal(0, _hasher.HashCount);
    }

    [Fact]
    public async Task Rejects_a_denied_password_and_states_the_rule()
    {
        var denied = "commonly-used-one";

        var result = await CreateService(denied).ExecuteAsync("alice", denied, CancellationToken.None);

        var invalid = Assert.IsType<CreateUserResult.InvalidPassword>(result);
        Assert.Equal(PasswordPolicy.Rules.CommonlyUsed, invalid.Rule);
        Assert.DoesNotContain(denied, invalid.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Never_records_a_creation_event_for_a_rejected_request()
    {
        await CreateService().ExecuteAsync("a", ValidPassword, CancellationToken.None);

        Assert.Empty(_events.UsersCreated);
    }
}
