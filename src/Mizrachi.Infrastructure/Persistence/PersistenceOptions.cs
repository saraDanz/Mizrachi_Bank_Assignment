using System.ComponentModel.DataAnnotations;

namespace Mizrachi.Infrastructure.Persistence;

/// <summary>
/// Which store backs the API. Selected by configuration only — there is no code change and no
/// conditional compilation involved in switching (NFR-1.3).
/// </summary>
public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    [Required(AllowEmptyStrings = false)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>Required by the file-backed providers; ignored by <see cref="Providers.InMemory"/>.</summary>
    public string? FilePath { get; set; }

    public static class Providers
    {
        public const string InMemory = "InMemory";
        public const string Sqlite = "Sqlite";
        public const string JsonFile = "JsonFile";
    }
}
