using Mizrachi.Application.Abstractions;

namespace Mizrachi.Application.UseCases;

/// <summary>
/// The outcome of validating a username and password.
/// </summary>
/// <remarks>
/// There is exactly one failure case, and that is the point. An unknown username and a wrong
/// password must be indistinguishable (FR-3.5); because the service cannot express which of the
/// two occurred, no caller — controller, logger or future maintainer — can accidentally reveal
/// it. The distinction does not leave the service because it has nowhere to go.
/// </remarks>
public abstract record ValidateUserResult
{
    private ValidateUserResult()
    {
    }

    public sealed record Authenticated(Guid UserId, string UserName, IssuedToken Token) : ValidateUserResult;

    public sealed record Rejected : ValidateUserResult;
}
