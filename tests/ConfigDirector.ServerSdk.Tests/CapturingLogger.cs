using Microsoft.Extensions.Logging;

namespace ConfigDirector.Tests;

internal sealed class CapturingLogger : ILogger
{
    internal List<(LogLevel Level, string Message, Exception? Error)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception), exception));
}
