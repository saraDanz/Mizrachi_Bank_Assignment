namespace Mizrachi.Application.Abstractions;

/// <summary>
/// Supplies server-generated identifiers (FR-1.2). A port rather than a direct call to
/// <see cref="Guid.NewGuid"/> so that tests can make identifiers predictable.
/// </summary>
public interface IIdGenerator
{
    Guid NewId();
}
