using Microsoft.Extensions.Logging;

namespace ConfigDirector.Tests;

internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    internal CapturingLogger Logger { get; } = new();

    public ILogger CreateLogger(string categoryName) => Logger;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}
