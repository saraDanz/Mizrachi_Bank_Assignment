namespace Mizrachi.Application.Abstractions;

/// <summary>
/// Records the security-relevant events of NFR-2.5: who did what, and when.
/// </summary>
/// <remarks>
/// <see cref="AuthenticationFailed"/> takes no parameters on purpose. The submitted username
/// must never be written to a log on a failed authentication — it may be a mistyped near-miss
/// credential, and it is personal data (NFR-2.3). A method that cannot receive it cannot leak
/// it, which makes the rule a property of the interface rather than something a future caller
/// has to remember.
/// </remarks>
public interface ISecurityEventLog
{
    void UserCreated(Guid userId);

    void UserDeleted(Guid userId);

    void AuthenticationSucceeded(Guid userId);

    void AuthenticationFailed();

    void AuthorizationRefused(Guid callerId, Guid targetUserId);
}
