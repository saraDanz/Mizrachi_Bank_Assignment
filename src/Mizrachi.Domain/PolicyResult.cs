namespace Mizrachi.Domain;

/// <summary>
/// The outcome of a policy check. On failure it names the rule that failed and states the
/// reason, so the caller can act on it (FR-5.7).
/// </summary>
/// <remarks>
/// A reason is a description of the <em>rule</em>, never of the submitted value: no reason
/// string ever echoes the password or any other credential back to the caller.
/// </remarks>
public readonly record struct PolicyResult(bool IsValid, string? FailedRule, string? Reason)
{
    public static PolicyResult Ok() => new(true, null, null);

    public static PolicyResult Fail(string rule, string reason) => new(false, rule, reason);
}
