namespace Mizrachi.Application.Abstractions;

/// <summary>
/// Issues the short-lived credential a caller uses to authorise a subsequent delete (FR-3.3).
/// </summary>
public interface ITokenIssuer
{
    IssuedToken Issue(Guid userId, string userName);
}

/// <summary>
/// A credential and the moment it stops being valid. It carries no password and no hash.
/// </summary>
public readonly record struct IssuedToken(string Token, DateTimeOffset ExpiresAt);
