using Microsoft.Extensions.Logging;
using Mizrachi.Application.Abstractions;

namespace Mizrachi.Infrastructure.Security;

/// <summary>
/// Writes the security events of NFR-2.5 to the application log.
/// </summary>
/// <remarks>
/// Every message is a compile-time template with only identifiers as parameters. No password,
/// token, request body or submitted username reaches a log line from here (NFR-2.3) — and the
/// failed-authentication event has no username available to write even if someone tried.
/// <para>
/// Audit records live in the application log for this exercise. Production would need an
/// append-only, tamper-evident store held separately (REQUIREMENTS §4.10).
/// </para>
/// </remarks>
public sealed class LoggingSecurityEventLog : ISecurityEventLog
{
    private readonly ILogger<LoggingSecurityEventLog> _logger;

    public LoggingSecurityEventLog(ILogger<LoggingSecurityEventLog> logger) => _logger = logger;

    public void UserCreated(Guid userId) =>
        _logger.LogInformation("Account created. UserId={UserId}", userId);

    public void UserDeleted(Guid userId) =>
        _logger.LogInformation("Account deleted. UserId={UserId}", userId);

    public void AuthenticationSucceeded(Guid userId) =>
        _logger.LogInformation("Authentication succeeded. UserId={UserId}", userId);

    public void AuthenticationFailed() =>
        _logger.LogWarning("Authentication failed.");

    public void AuthorizationRefused(Guid callerId, Guid targetUserId) =>
        _logger.LogWarning(
            "Authorisation refused. CallerId={CallerId} TargetUserId={TargetUserId}",
            callerId,
            targetUserId);
}
