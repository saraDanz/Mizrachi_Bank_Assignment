using Mizrachi.Application.Abstractions;

namespace Mizrachi.Infrastructure.Time;

/// <summary>The real clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Server-generated identifiers (FR-1.2). <see cref="Guid.NewGuid"/> is version 4 — random,
/// not sequential, so identifiers are not enumerable (REQUIREMENTS §3.1).
/// </summary>
public sealed class GuidIdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.NewGuid();
}
