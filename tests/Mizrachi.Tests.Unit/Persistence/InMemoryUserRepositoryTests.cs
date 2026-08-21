using Mizrachi.Infrastructure.Persistence;

namespace Mizrachi.Tests.Unit.Persistence;

/// <summary>The contract suite, run against the in-memory store.</summary>
public sealed class InMemoryUserRepositoryTests : UserRepositoryContractTests
{
    protected override IReadOnlyDictionary<string, string?> ProviderConfiguration() =>
        new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = PersistenceOptions.Providers.InMemory
        };
}
