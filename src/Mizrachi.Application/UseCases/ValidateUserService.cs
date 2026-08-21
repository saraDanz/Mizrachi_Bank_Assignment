using Mizrachi.Application.Abstractions;
using Mizrachi.Domain;

namespace Mizrachi.Application.UseCases;

/// <summary>
/// Validates a username and password (FR-3.1–3.8).
/// </summary>
/// <remarks>
/// Every path through <see cref="ExecuteAsync"/> performs one repository lookup and one hash
/// verification, whether or not the account exists. That is the requirement, not an
/// optimisation: the work done must not depend on whether the username was found (FR-3.6), and
/// the single <see cref="ValidateUserResult.Rejected"/> case means the caller cannot tell which
/// of the two failures occurred (FR-3.5).
/// </remarks>
public sealed class ValidateUserService
{
    private readonly IUserRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenIssuer _tokenIssuer;
    private readonly ISecurityEventLog _securityEventLog;

    /// <summary>
    /// A hash of a random password, computed once. It gives the unknown-username path a real
    /// hash to verify against, so that path costs what the found path costs.
    /// </summary>
    private readonly string _absentUserHash;

    public ValidateUserService(
        IUserRepository repository,
        IPasswordHasher passwordHasher,
        ITokenIssuer tokenIssuer,
        ISecurityEventLog securityEventLog)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _tokenIssuer = tokenIssuer;
        _securityEventLog = securityEventLog;

        _absentUserHash = passwordHasher.Hash(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
    }

    public async Task<ValidateUserResult> ExecuteAsync(
        string? userName,
        string? password,
        CancellationToken cancellationToken)
    {
        var candidatePassword = password ?? string.Empty;

        // Defence in depth. The API rejects an over-length password before reaching this
        // service, but the bound on hashing work must not depend on a caller remembering to.
        if (candidatePassword.Length > PasswordPolicy.MaxLength)
        {
            _securityEventLog.AuthenticationFailed();
            return new ValidateUserResult.Rejected();
        }

        var user = await _repository.FindByUserNameAsync(
            UserNamePolicy.Normalize(userName),
            cancellationToken);

        // Unconditional: a miss verifies against the absent-user hash rather than returning
        // early, so a wrong password and an unknown username cost the same (FR-3.6).
        var verification = _passwordHasher.Verify(user?.UserPassword ?? _absentUserHash, candidatePassword);

        if (user is null || verification == PasswordVerification.Failed)
        {
            _securityEventLog.AuthenticationFailed();
            return new ValidateUserResult.Rejected();
        }

        _securityEventLog.AuthenticationSucceeded(user.UserId);

        return new ValidateUserResult.Authenticated(
            user.UserId,
            user.UserName,
            _tokenIssuer.Issue(user.UserId, user.UserName));
    }
}
