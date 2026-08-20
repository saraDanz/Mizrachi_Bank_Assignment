using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Mizrachi.Domain;
using Mizrachi.Infrastructure.Persistence;

namespace Mizrachi.Tests.Unit.Persistence;

/// <summary>
/// The contract suite, run against the JSON file store on a throwaway file per test.
/// </summary>
public sealed class JsonFileUserRepositoryTests : UserRepositoryContractTests
{
    private readonly string _filePath =
        Path.Combine(Path.GetTempPath(), "mizrachi-tests", $"{Guid.NewGuid():N}.json");

    protected override IReadOnlyDictionary<string, string?> ProviderConfiguration() =>
        new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = PersistenceOptions.Providers.JsonFile,
            ["Persistence:FilePath"] = _filePath
        };

    protected override void OnStoreCreated(IServiceProvider services) =>
        services.GetRequiredService<IDatabaseInitializer>()
            .InitializeAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    protected override void DisposeStore()
    {
        foreach (var path in new[] { _filePath, _filePath + ".tmp" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Throwaway temp file.
            }
        }
    }

    [Fact]
    public async Task Leaves_no_temporary_file_behind_after_a_write()
    {
        // The atomic write moves its temp file into place; a leftover would mean a write path
        // that can be interrupted into leaving a partial file.
        await Repository.TryAddAsync(
            User.Create(Guid.NewGuid(), "alice", "hashed:alice"),
            CancellationToken.None);

        Assert.False(File.Exists(_filePath + ".tmp"));
    }

    [Fact]
    public async Task Writes_a_file_that_is_valid_json()
    {
        await Repository.TryAddAsync(
            User.Create(Guid.NewGuid(), "alice", "hashed:alice"),
            CancellationToken.None);

        var document = JsonDocument.Parse(await File.ReadAllTextAsync(_filePath));

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(1, document.RootElement.GetArrayLength());
    }
}
