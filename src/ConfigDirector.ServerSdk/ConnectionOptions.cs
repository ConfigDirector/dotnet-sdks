namespace ConfigDirector;

/// <summary>
/// How the client connects to ConfigDirector.
/// </summary>
/// <remarks>
/// Every setting is checked as it is assigned, so an unusable one is reported where it was written
/// rather than as a client that quietly never updates. There are two exceptions. A
/// <see cref="PollingInterval"/> below <see cref="MinPollingInterval"/> is kept as written, and the
/// client raises it to the minimum with a warning. <see cref="File"/> and <see cref="Values"/> may be
/// assigned before the mode they depend on, so they are read when the client is built and an
/// unusable one is reported when it initializes.
/// </remarks>
public sealed class ConnectionOptions
{
    // The longest a CancellationTokenSource can be scheduled to fire, which is what both durations
    // are ultimately handed to.
    private static readonly TimeSpan LongestDuration = TimeSpan.FromMilliseconds(int.MaxValue);

    private ConnectionMode _mode = ConnectionMode.Streaming;
    private TimeSpan _pollingInterval = DefaultPollingInterval;
    private TimeSpan _timeout = TimeSpan.FromSeconds(3);
    private Uri? _url;

    /// <summary>How often the client polls when no interval is set: 5 minutes.</summary>
    public static TimeSpan DefaultPollingInterval { get; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The shortest interval the client polls on: 1 minute. A <see cref="PollingInterval"/> below
    /// it is raised to it.
    /// </summary>
    public static TimeSpan MinPollingInterval { get; } = TimeSpan.FromMinutes(1);

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
    /// How long to wait between polls. Used only in <see cref="ConnectionMode.Polling"/>. Defaults
    /// to <see cref="DefaultPollingInterval"/>, 5 minutes; the minimum is
    /// <see cref="MinPollingInterval"/>, 1 minute. A value below the minimum is raised to the
    /// minimum and a warning is logged when the client is built.
    /// </summary>
    public TimeSpan PollingInterval
    {
        get => _pollingInterval;
        set => _pollingInterval = NoLongerThanTheSdkCanWait(value, nameof(PollingInterval));
    }

    /// <summary>
    /// How long initialization waits for the first config state, and how long any one request to
    /// ConfigDirector may take. Defaults to 3 seconds, and must be positive.
    /// </summary>
    /// <remarks>
    /// Running out is not an error: <see cref="IConfigDirectorClient.IsReady"/> is what says
    /// whether config state arrived, and in <see cref="ConnectionMode.Streaming"/> the client keeps
    /// trying in the background. It has no effect in <see cref="ConnectionMode.Offline"/>.
    /// </remarks>
    public TimeSpan Timeout
    {
        get => _timeout;
        set => _timeout = Usable(value, nameof(Timeout));
    }

    /// <summary>
    /// The base URL to connect to, or null for the ConfigDirector service. Only needed when routing
    /// through a proxy. Must be absolute. It has no effect in <see cref="ConnectionMode.Offline"/>.
    /// </summary>
    public Uri? Url
    {
        get => _url;
        set => _url = value is null || value.IsAbsoluteUri
            ? value
            : throw new ArgumentException($"The connection URL '{value}' must be absolute.", nameof(value));
    }

    /// <summary>
    /// The path of a JSON file holding the values to serve in <see cref="ConnectionMode.Offline"/>,
    /// resolved against the working directory of the process. The file holds one JSON object with a
    /// value per config key. An empty or whitespace path counts as no file.
    /// </summary>
    /// <remarks>
    /// Each value's config type follows from the value: <c>true</c> or <c>false</c> is a boolean, a
    /// whole number that fits in a <see cref="long"/>, such as <c>20</c> or <c>20.0</c>, an integer,
    /// any other number a float, a string a string, and an object or an array JSON. <c>null</c> is
    /// refused. Give either this or
    /// <see cref="Values"/>, and only in <see cref="ConnectionMode.Offline"/>; anything else makes
    /// <see cref="IConfigDirectorClient.InitializeAsync"/> throw
    /// <see cref="ConfigDirectorInitializationException"/>.
    /// </remarks>
    public string? File { get; set; }

    /// <summary>
    /// The values to serve in <see cref="ConnectionMode.Offline"/>, keyed by config key. The client
    /// takes a copy when it is built.
    /// </summary>
    /// <remarks>
    /// Each value's config type follows from its .NET type: a <see cref="bool"/> is a boolean; an
    /// <see cref="sbyte"/>, <see cref="byte"/>, <see cref="short"/>, <see cref="ushort"/>,
    /// <see cref="int"/>, <see cref="uint"/> or <see cref="long"/> an integer; a
    /// <see cref="float"/>, <see cref="double"/> or <see cref="decimal"/> a float; a
    /// <see cref="string"/> a string; and a dictionary with string keys, a list, or a
    /// <see cref="System.Text.Json.JsonElement"/> or <see cref="System.Text.Json.Nodes.JsonNode"/>
    /// holding an object or an array JSON. <c>null</c> and any other type are refused. Give either
    /// this or <see cref="File"/>, and only in <see cref="ConnectionMode.Offline"/>; anything else
    /// makes <see cref="IConfigDirectorClient.InitializeAsync"/> throw
    /// <see cref="ConfigDirectorInitializationException"/>.
    /// </remarks>
    public IReadOnlyDictionary<string, object>? Values { get; set; }

    private static TimeSpan Usable(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(name, value, $"The {name} must be a positive duration.");
        }

        return NoLongerThanTheSdkCanWait(value, name);
    }

    private static TimeSpan NoLongerThanTheSdkCanWait(TimeSpan value, string name)
    {
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
