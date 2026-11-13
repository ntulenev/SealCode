using Microsoft.Extensions.Logging;

namespace SealCode.Tests;

internal sealed class AuditLogProvider : ILoggerProvider
{
    public List<(LogLevel Level, string Text)> Entries { get; } = [];
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);
    public void Dispose() { }

    private sealed class CaptureLogger(AuditLogProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Entries.Add((logLevel, formatter(state, exception)));
    }
}
