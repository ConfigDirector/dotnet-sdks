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
    private ConnectionOptions _connection = new();
    private TelemetryOptions _telemetry = new();
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    /// <summary>
    /// Describes the calling application. Supplying it lets targeting rules match on the
    /// application's name and version.
    /// </summary>
    public Metadata? Metadata { get; set; }

    /// <summary>
    /// How the client connects to ConfigDirector. Populate it in place, or assign one you already
    /// hold; assigning null throws.
    /// </summary>
    public ConnectionOptions Connection
    {
        get => _connection;
        set => _connection = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// How evaluations are reported back to ConfigDirector. The defaults suit most applications.
    /// Populate it in place, or assign one you already hold; assigning null throws.
    /// </summary>
    public TelemetryOptions Telemetry
    {
        get => _telemetry;
        set => _telemetry = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Where the SDK writes. Defaults to <see cref="NullLoggerFactory"/>, which discards everything.
    /// </summary>
    public ILoggerFactory LoggerFactory
    {
        get => _loggerFactory;
        set => _loggerFactory = value ?? throw new ArgumentNullException(nameof(value));
    }
}
