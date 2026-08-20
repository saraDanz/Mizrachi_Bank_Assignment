using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mizrachi.Tests.Integration;

/// <summary>
/// An <see cref="ApiFactory"/> whose log output is captured, at Trace, into a provider the test
/// can read. Environment and settings pass straight through, so a test can capture the log of a
/// host that is deliberately misconfigured.
/// </summary>
internal sealed class LoggingApiFactory : ApiFactory
{
    private readonly ILoggerProvider _provider;

    public LoggingApiFactory(
        ILoggerProvider provider,
        string environment = "Development",
        IReadOnlyDictionary<string, string?>? settings = null)
        : base(environment, settings) =>
        _provider = provider;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
            services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(_provider);
            }));
    }
}
