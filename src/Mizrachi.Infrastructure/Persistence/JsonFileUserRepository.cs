using System.Text.Json;
using Mizrachi.Application.Abstractions;
using Mizrachi.Domain;

namespace Mizrachi.Infrastructure.Persistence;

/// <summary>
/// Durable store backed by a JSON file (NFR-1.2).
/// </summary>
/// <remarks>
/// <b>Its uniqueness guarantee is process-local (OQ-7).</b> Inside one process a semaphore
/// serialises every read-modify-write, so FR-1.8 holds. Across two processes sharing one file
/// there is no atomic compare-and-insert to appeal to, and two writers could both believe they
/// won. SQLite is the durable provider to reach for; this one demonstrates that the repository
/// port is genuinely provider-agnostic.
/// </remarks>
internal sealed class JsonFileUserRepository : IUserRepository, IDatabaseInitializer, IDisposable
{
    private sealed record StoredUser(Guid UserId, string UserName, string UserPassword);

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;

    public JsonFileUserRepository(string filePath) => _filePath = filePath;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(_filePath))
        {
            await WriteAsync(new List<StoredUser>(), cancellationToken);
        }
    }

    public async Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = (await ReadAsync(cancellationToken))
                .FirstOrDefault(user => UserNameComparer.Instance.Equals(user.UserName, userName));

            return stored is null ? null : ToUser(stored);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = (await ReadAsync(cancellationToken))
                .FirstOrDefault(user => user.UserId == userId);

            return stored is null ? null : ToUser(stored);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryAddAsync(User user, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await ReadAsync(cancellationToken);

            // The check and the write are one critical section, so this is not the
            // check-then-act race FR-1.8 forbids — within this process.
            if (users.Any(existing => UserNameComparer.Instance.Equals(existing.UserName, user.UserName)))
            {
                return false;
            }

            users.Add(new StoredUser(user.UserId, user.UserName, user.UserPassword));
            await WriteAsync(users, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await ReadAsync(cancellationToken);

            if (users.RemoveAll(user => user.UserId == userId) == 0)
            {
                return false;
            }

            // A hard delete: the record leaves the file entirely, taking the stored hash with
            // it, rather than being flagged inactive (FR-2.7).
            await WriteAsync(users, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<StoredUser>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new List<StoredUser>();
        }

        await using var stream = File.OpenRead(_filePath);

        if (stream.Length == 0)
        {
            return new List<StoredUser>();
        }

        return await JsonSerializer.DeserializeAsync<List<StoredUser>>(stream, SerializerOptions, cancellationToken)
               ?? new List<StoredUser>();
    }

    /// <remarks>
    /// Written to a sibling temp file and moved into place, so a process killed mid-write
    /// leaves either the previous file or the new one — never a half-written one.
    /// </remarks>
    private async Task WriteAsync(List<StoredUser> users, CancellationToken cancellationToken)
    {
        var temporaryPath = _filePath + ".tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, users, SerializerOptions, cancellationToken);
        }

        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    private static User ToUser(StoredUser stored) =>
        User.Create(stored.UserId, stored.UserName, stored.UserPassword);

    public void Dispose() => _gate.Dispose();
}
