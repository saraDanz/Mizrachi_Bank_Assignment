namespace Mizrachi.Application.Abstractions;

/// <summary>
/// The current time, as a port, so token lifetimes can be tested without waiting.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
