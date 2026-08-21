using Mizrachi.Application.UseCases;
using Mizrachi.Domain;
using Mizrachi.Tests.Unit.Fakes;

namespace Mizrachi.Tests.Unit.UseCases;

public sealed class DeleteUserServiceTests
{
    private readonly FakeUserRepository _repository = new();
    private readonly RecordingSecurityEventLog _events = new();
    private readonly Guid _ownerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private readonly Guid _otherUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private DeleteUserService CreateService()
    {
        _repository.Seed(User.Create(_ownerId, "alice", "hashed:YWxpY2U="));
        _repository.Seed(User.Create(_otherUserId, "bob", "hashed:Ym9i"));
        return new DeleteUserService(_repository, _events);
    }

    [Fact]
    public async Task Deletes_the_callers_own_account()
    {
        var result = await CreateService().ExecuteAsync(_ownerId, _ownerId, CancellationToken.None);

        Assert.IsType<DeleteUserResult.Deleted>(result);
        Assert.Equal(new[] { _ownerId }, _events.UsersDeleted);
    }

    [Fact]
    public async Task Refuses_an_account_the_caller_does_not_own()
    {
        var result = await CreateService().ExecuteAsync(_ownerId, _otherUserId, CancellationToken.None);

        Assert.IsType<DeleteUserResult.Forbidden>(result);
        Assert.Single(_events.AuthorizationRefusals);
    }

    [Fact]
    public async Task Consults_no_datastore_when_refusing_an_unowned_identifier()
    {
        // FR-2.4: authorisation is evaluated before existence, so there is no lookup whose
        // outcome could differ between a real and an imaginary identifier.
        var service = CreateService();

        await service.ExecuteAsync(_ownerId, _otherUserId, CancellationToken.None);

        Assert.Equal(0, _repository.FindByIdCalls);
        Assert.Equal(0, _repository.DeleteCalls);
    }

    [Fact]
    public async Task Refuses_a_real_and_an_unknown_identifier_identically()
    {
        var service = CreateService();

        var unownedButReal = await service.ExecuteAsync(_ownerId, _otherUserId, CancellationToken.None);
        var neverIssued = await service.ExecuteAsync(_ownerId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(unownedButReal, neverIssued);
        Assert.Equal(0, _repository.DeleteCalls);
    }

    [Fact]
    public async Task Reports_not_found_when_the_caller_deletes_their_own_account_twice()
    {
        // FR-2.6: deletion is not idempotent by design.
        var service = CreateService();
        await service.ExecuteAsync(_ownerId, _ownerId, CancellationToken.None);

        var result = await service.ExecuteAsync(_ownerId, _ownerId, CancellationToken.None);

        Assert.IsType<DeleteUserResult.NotFound>(result);
        Assert.Single(_events.UsersDeleted);
    }

    [Fact]
    public async Task Refuses_an_unauthenticated_caller()
    {
        var result = await CreateService().ExecuteAsync(Guid.Empty, Guid.Empty, CancellationToken.None);

        Assert.IsType<DeleteUserResult.Forbidden>(result);
        Assert.Equal(0, _repository.DeleteCalls);
    }
}
