using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mizrachi.Api.Contracts;
using Mizrachi.Api.Errors;
using Mizrachi.Application.UseCases;

namespace Mizrachi.Api.Controllers;

[ApiController]
[Route("api/users")]
[Produces("application/json")]
public sealed class UsersController : ControllerBase
{
    private readonly CreateUserService _createUser;
    private readonly ValidateUserService _validateUser;
    private readonly DeleteUserService _deleteUser;

    public UsersController(
        CreateUserService createUser,
        ValidateUserService validateUser,
        DeleteUserService deleteUser)
    {
        _createUser = createUser;
        _validateUser = validateUser;
        _deleteUser = deleteUser;
    }

    /// <summary>Creates an account (FR-1.1). Open to unauthenticated callers (FR-1.10).</summary>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CreateUserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await _createUser.ExecuteAsync(request.UserName, request.Password, cancellationToken);

        return result switch
        {
            CreateUserResult.Created created => Created(
                $"/api/users/{created.UserId}",
                new CreateUserResponse(created.UserId, created.UserName)),

            CreateUserResult.InvalidUserName invalid =>
                BadRequest(ApiProblemDetails.Invalid(HttpContext, invalid.Rule, invalid.Reason)),

            CreateUserResult.InvalidPassword invalid =>
                BadRequest(ApiProblemDetails.Invalid(HttpContext, invalid.Rule, invalid.Reason)),

            CreateUserResult.DuplicateUserName =>
                Conflict(ApiProblemDetails.Conflict(HttpContext, "That username is already taken.")),

            _ => throw new InvalidOperationException("Unhandled create-user result.")
        };
    }

    /// <summary>
    /// Validates credentials (FR-3.1). Credentials arrive in the body and nowhere else (FR-3.2).
    /// </summary>
    [HttpPost("validate")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ValidateUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Validate(ValidateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await _validateUser.ExecuteAsync(request.UserName, request.Password, cancellationToken);

        // One failure case in, one response out: there is no branch here that could answer an
        // unknown username differently from a wrong password (FR-3.5).
        return result switch
        {
            ValidateUserResult.Authenticated authenticated => Ok(new ValidateUserResponse(
                authenticated.UserId,
                authenticated.UserName,
                authenticated.Token.Token,
                authenticated.Token.ExpiresAt)),

            _ => Unauthorized(ApiProblemDetails.Unauthorized(HttpContext))
        };
    }

    /// <summary>Deletes the caller's own account (FR-2.1). Requires authentication (FR-2.2).</summary>
    [HttpDelete("{userId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid userId, CancellationToken cancellationToken)
    {
        var result = await _deleteUser.ExecuteAsync(CallerId(), userId, cancellationToken);

        return result switch
        {
            DeleteUserResult.Deleted => NoContent(),

            DeleteUserResult.Forbidden =>
                StatusCode(StatusCodes.Status403Forbidden, ApiProblemDetails.Forbidden(HttpContext)),

            DeleteUserResult.NotFound => NotFound(ApiProblemDetails.NotFound(HttpContext)),

            _ => throw new InvalidOperationException("Unhandled delete-user result.")
        };
    }

    /// <remarks>
    /// The caller's identity comes from the validated token's subject and from nowhere else —
    /// not a body field, not a header, not the route (FR-2.3).
    /// </remarks>
    private Guid CallerId() =>
        Guid.TryParse(User.FindFirstValue("sub"), out var callerId) ? callerId : Guid.Empty;
}
