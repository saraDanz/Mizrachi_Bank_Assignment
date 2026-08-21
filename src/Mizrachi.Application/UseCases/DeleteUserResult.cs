namespace Mizrachi.Application.UseCases;

/// <summary>
/// The outcome of deleting a user.
/// </summary>
/// <remarks>
/// <see cref="Forbidden"/> is returned for any identifier the caller does not own, whether or
/// not it exists, because authorisation is evaluated before existence (FR-2.4). A caller
/// therefore cannot use this endpoint to discover which identifiers are real.
/// </remarks>
public abstract record DeleteUserResult
{
    private DeleteUserResult()
    {
    }

    public sealed record Deleted : DeleteUserResult;

    /// <summary>The target is not the caller's own account (FR-2.3).</summary>
    public sealed record Forbidden : DeleteUserResult;

    /// <summary>
    /// Reachable only for the caller's own, already-deleted account (FR-2.5). Deletion is not
    /// idempotent by design, so a repeat delete reports this rather than success (FR-2.6).
    /// </summary>
    public sealed record NotFound : DeleteUserResult;
}
