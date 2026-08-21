using Microsoft.Extensions.Logging;

namespace Mizrachi.Tests.Integration;

/// <summary>
/// Hand-written logger that keeps every formatted message. No mocking library.
/// </summary>
/// <remarks>
/// Shared, because more than one security property is stated as "this never reaches the log" and
/// each of them needs the same evidence: the actual text the host wrote.
/// </remarks>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _written = new();

    public IReadOnlyList<string> Written
    {
        get
        {
            lock (_written)
            {
                return _written.ToList();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(_written);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<string> _written;

        public RecordingLogger(List<string> written) => _written = written;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_written)
            {
                _written.Add(formatter(state, exception));

                if (exception is not null)
                {
                    _written.Add(exception.ToString());
                }
            }
        }
    }
}
