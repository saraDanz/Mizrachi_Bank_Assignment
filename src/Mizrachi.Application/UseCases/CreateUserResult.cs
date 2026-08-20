namespace Mizrachi.Application.UseCases;

/// <summary>
/// The outcome of creating a user. A closed hierarchy: the private constructor means only the
/// nested cases below can derive from it, so a caller that handles all of them has handled
/// every possible outcome.
/// </summary>
public abstract record CreateUserResult
{
    private CreateUserResult()
    {
    }

    /// <summary>The password is absent from this result, as it is from every response (FR-1.4).</summary>
    public sealed record Created(Guid UserId, string UserName) : CreateUserResult;

    public sealed record InvalidUserName(string Rule, string Reason) : CreateUserResult;

    public sealed record InvalidPassword(string Rule, string Reason) : CreateUserResult;

    /// <summary>
    /// The username is taken. Distinct from a validation failure (FR-1.7), and decided by the
    /// datastore rather than by a prior existence check (FR-1.8).
    /// </summary>
    public sealed record DuplicateUserName : CreateUserResult;
}
