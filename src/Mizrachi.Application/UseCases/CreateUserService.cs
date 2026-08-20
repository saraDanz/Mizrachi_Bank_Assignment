using Mizrachi.Application.Abstractions;
using Mizrachi.Domain;

namespace Mizrachi.Application.UseCases;

/// <summary>
/// Creates a user account (FR-1.1–1.10).
/// </summary>
public sealed class CreateUserService
{
    private readonly IUserRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PasswordPolicy _passwordPolicy;
    private readonly UserNamePolicy _userNamePolicy;
    private readonly IIdGenerator _idGenerator;
    private readonly ISecurityEventLog _securityEventLog;

    public CreateUserService(
        IUserRepository repository,
        IPasswordHasher passwordHasher,
        PasswordPolicy passwordPolicy,
        UserNamePolicy userNamePolicy,
        IIdGenerator idGenerator,
        ISecurityEventLog securityEventLog)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
        _passwordPolicy = passwordPolicy;
        _userNamePolicy = userNamePolicy;
        _idGenerator = idGenerator;
        _securityEventLog = securityEventLog;
    }

    public async Task<CreateUserResult> ExecuteAsync(
        string? userName,
        string? password,
        CancellationToken cancellationToken)
    {
        var userNameCheck = _userNamePolicy.Validate(userName);
        if (!userNameCheck.IsValid)
        {
            return new CreateUserResult.InvalidUserName(userNameCheck.FailedRule!, userNameCheck.Reason!);
        }

        var normalizedUserName = UserNamePolicy.Normalize(userName);

        // The policy runs before the hasher, so an over-length password never reaches an
        // iterated hash function (FR-5.2).
        var passwordCheck = _passwordPolicy.Validate(password, normalizedUserName);
        if (!passwordCheck.IsValid)
        {
            return new CreateUserResult.InvalidPassword(passwordCheck.FailedRule!, passwordCheck.Reason!);
        }

        var user = User.Create(
            _idGenerator.NewId(),
            normalizedUserName,
            _passwordHasher.Hash(password!));

        // Uniqueness is the datastore's answer, not the result of a prior lookup (FR-1.8).
        if (!await _repository.TryAddAsync(user, cancellationToken))
        {
            return new CreateUserResult.DuplicateUserName();
        }

        _securityEventLog.UserCreated(user.UserId);

        return new CreateUserResult.Created(user.UserId, user.UserName);
    }
}
