using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigDirector;

/// <summary>
/// Settings for a <see cref="ConfigDirectorClient"/>, read once when the client is built.
/// </summary>
/// <remarks>
/// <code>
/// new ConfigDirectorClient(serverSdkKey, new ConfigDirectorClientOptions
/// {
///     Metadata = new Metadata { AppName = "checkout", AppVersion = "1.2.3" },
///     LoggerFactory = loggerFactory,
///     Connection =
///     {
///         Mode = ConnectionMode.Polling,
///         PollingInterval = TimeSpan.FromSeconds(30),
///     },
/// })
/// </code>
/// </remarks>
public sealed class ConfigDirectorClientOptions
{
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    /// <summary>
    /// Describes the calling application. Supplying it lets targeting rules match on the
    /// application's name and version.
    /// </summary>
    public Metadata? Metadata { get; set; }

    /// <summary>How the client connects to ConfigDirector.</summary>
    public ConnectionOptions Connection { get; } = new();

    /// <summary>
    /// How evaluations are reported back to ConfigDirector. The defaults suit most applications.
    /// </summary>
    public TelemetryOptions Telemetry { get; } = new();

    /// <summary>
    /// Where the SDK writes. Defaults to <see cref="NullLoggerFactory"/>, which discards everything.
    /// </summary>
    public ILoggerFactory LoggerFactory
    {
        get => _loggerFactory;
        set => _loggerFactory = value ?? throw new ArgumentNullException(nameof(value));
    }
}
