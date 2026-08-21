using Mizrachi.Application.Abstractions;

namespace Mizrachi.Application.UseCases;

/// <summary>
/// Deletes the caller's own account (FR-2.1–2.7).
/// </summary>
public sealed class DeleteUserService
{
    private readonly IUserRepository _repository;
    private readonly ISecurityEventLog _securityEventLog;

    public DeleteUserService(IUserRepository repository, ISecurityEventLog securityEventLog)
    {
        _repository = repository;
        _securityEventLog = securityEventLog;
    }

    public async Task<DeleteUserResult> ExecuteAsync(
        Guid callerId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        // Authorisation is evaluated before existence (FR-2.4). The repository is not consulted
        // at all for an identifier the caller does not own, so there is no lookup result that
        // could leak whether that identifier is real — the refusal is identical either way.
        if (callerId == Guid.Empty || callerId != targetUserId)
        {
            _securityEventLog.AuthorizationRefused(callerId, targetUserId);
            return new DeleteUserResult.Forbidden();
        }

        if (!await _repository.DeleteAsync(targetUserId, cancellationToken))
        {
            return new DeleteUserResult.NotFound();
        }

        _securityEventLog.UserDeleted(targetUserId);

        return new DeleteUserResult.Deleted();
    }
}
