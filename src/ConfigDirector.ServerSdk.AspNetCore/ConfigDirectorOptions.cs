namespace ConfigDirector;

/// <summary>
/// Settings for the client that <c>AddConfigDirector</c> builds, bound from configuration.
/// </summary>
/// <remarks>
/// <para>
/// This is the configuration-facing counterpart of <see cref="ConfigDirectorClientOptions"/>: it
/// holds the same <see cref="ConnectionOptions"/> and <see cref="TelemetryOptions"/> the SDK itself
/// defines, adds the server SDK key, and leaves the logger factory to dependency injection.
/// </para>
/// <code>
/// "ConfigDirector": {
///   "ServerSdkKey": "...",
///   "Connection": { "Mode": "Polling", "PollingInterval": "00:01:00" }
/// }
/// </code>
/// <para>
/// The client reads these once, when it is first resolved, so a configuration reload does not
/// reach a client that has already been built.
/// </para>
/// </remarks>
public sealed class ConfigDirectorOptions
{
    /// <summary>The configuration section bound when no other one is given.</summary>
    public const string SectionName = "ConfigDirector";

    /// <summary>
    /// Your server SDK key. A secret: supply it as an environment variable, a user secret, or from
    /// a secret store in code, rather than committing it in <c>appsettings.json</c>.
    /// </summary>
    public string? ServerSdkKey { get; set; }

    /// <summary>
    /// The application's name, which targeting rules can match on. Defaults to the host's
    /// <see cref="Microsoft.Extensions.Hosting.IHostEnvironment.ApplicationName"/>.
    /// </summary>
    public string? AppName { get; set; }

    /// <summary>
    /// The version the application is running, matched by semver rules. Defaults to the entry
    /// assembly's informational version, with any build metadata suffix removed.
    /// </summary>
    public string? AppVersion { get; set; }

    /// <summary>
    /// Whether a host that has not reached ConfigDirector by the end of startup fails to start.
    /// Defaults to false, which is the SDK's own posture: the application starts, a warning is
    /// logged, and every config resolves to the default its caller supplied until config state
    /// arrives.
    /// </summary>
    public bool RequireReadyOnStartup { get; set; }

    /// <summary>How the client connects to ConfigDirector.</summary>
    public ConnectionOptions Connection { get; } = new();

    /// <summary>
    /// How evaluations are reported back to ConfigDirector. The defaults suit most applications.
    /// </summary>
    public TelemetryOptions Telemetry { get; } = new();
}
