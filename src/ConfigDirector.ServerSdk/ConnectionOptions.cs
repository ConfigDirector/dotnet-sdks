namespace ConfigDirector;

/// <summary>
/// How the client connects to ConfigDirector.
/// </summary>
/// <remarks>
/// Every setting is checked as it is assigned, so an unusable one is reported where it was written
/// rather than as a client that quietly never updates.
/// </remarks>
public sealed class ConnectionOptions
{
    // The longest a CancellationTokenSource can be scheduled to fire, which is what both durations
    // are ultimately handed to.
    private static readonly TimeSpan LongestDuration = TimeSpan.FromMilliseconds(int.MaxValue);

    private ConnectionMode _mode = ConnectionMode.Streaming;
    private TimeSpan _pollingInterval = TimeSpan.FromSeconds(60);
    private TimeSpan _timeout = TimeSpan.FromSeconds(3);
    private Uri? _url;

    /// <summary>
    /// How the client keeps its config state current. Defaults to
    /// <see cref="ConnectionMode.Streaming"/>.
    /// </summary>
    public ConnectionMode Mode
    {
        get => _mode;
        set => _mode = Enum.IsDefined(typeof(ConnectionMode), value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The connection mode is not one of the defined modes.");
    }

    /// <summary>
    /// How long to wait between polls. Used only in <see cref="ConnectionMode.Polling"/>; defaults
    /// to 60 seconds. Must be positive: a client that polls needs an interval to poll on, and
    /// <see cref="ConnectionMode.OneTime"/> is how to ask for a single fetch instead.
    /// </summary>
    public TimeSpan PollingInterval
    {
        get => _pollingInterval;
        set => _pollingInterval = Usable(value, nameof(PollingInterval));
    }

    /// <summary>
    /// How long initialization waits for the first config state, and how long any one request to
    /// ConfigDirector may take. Defaults to 3 seconds, and must be positive.
    /// </summary>
    /// <remarks>
    /// Running out is not an error: <see cref="IConfigDirectorClient.IsReady"/> is what says
    /// whether config state arrived, and in <see cref="ConnectionMode.Streaming"/> the client keeps
    /// trying in the background.
    /// </remarks>
    public TimeSpan Timeout
    {
        get => _timeout;
        set => _timeout = Usable(value, nameof(Timeout));
    }

    /// <summary>
    /// The base URL to connect to, or null for the ConfigDirector service. Only needed when routing
    /// through a proxy. Must be absolute.
    /// </summary>
    public Uri? Url
    {
        get => _url;
        set => _url = value is null || value.IsAbsoluteUri
            ? value
            : throw new ArgumentException($"The connection URL '{value}' must be absolute.", nameof(value));
    }

    private static TimeSpan Usable(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name, value, $"The {name} must be a positive duration.");
        }

        if (value > LongestDuration)
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                $"The {name} must be no longer than {LongestDuration}, which is the longest the SDK can wait.");
        }

        return value;
    }
}
