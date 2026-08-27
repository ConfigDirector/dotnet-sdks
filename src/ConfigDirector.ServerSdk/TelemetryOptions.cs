namespace ConfigDirector;

/// <summary>
/// Telemetry tuning. It is unlikely these need adjusting: they trade memory footprint against how
/// often telemetry requests are made, which only matters for an application performing a large
/// number of evaluations per second.
/// </summary>
/// <remarks>
/// ConfigDirector relies on these events to power the insights and config usage features in the
/// dashboard. Every setting is checked as it is assigned, so an unusable one is reported where it
/// was written.
/// </remarks>
public sealed class TelemetryOptions
{
    /// <summary>The queue limit used when none is set.</summary>
    public const int DefaultEventQueueLimit = 5_000;

    /// <summary>The smallest queue limit accepted.</summary>
    public const int MinEventQueueLimit = 100;

    /// <summary>The largest queue limit accepted.</summary>
    public const int MaxEventQueueLimit = 100_000;

    private int _eventQueueLimit = DefaultEventQueueLimit;
    private TimeSpan _flushInterval = DefaultFlushInterval;

    /// <summary>How often events are reported when no interval is set.</summary>
    public static TimeSpan DefaultFlushInterval { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many events are held between flushes. When the limit is reached before events are sent,
    /// the oldest are dropped. Defaults to <see cref="DefaultEventQueueLimit"/>, and must be
    /// between <see cref="MinEventQueueLimit"/> and <see cref="MaxEventQueueLimit"/>.
    /// </summary>
    /// <remarks>
    /// ConfigDirector keeps a count of dropped events, and raises a dashboard notification when
    /// more than half of an application's events are being dropped.
    /// </remarks>
    public int EventQueueLimit
    {
        get => _eventQueueLimit;
        set => _eventQueueLimit = value is >= MinEventQueueLimit and <= MaxEventQueueLimit
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"The telemetry event queue limit must be between {MinEventQueueLimit} and {MaxEventQueueLimit}.");
    }

    /// <summary>
    /// How often events are reported over the network. Defaults to 30 seconds, and must be
    /// positive. Shorten it for an application that captures a large number of events in short
    /// bursts, to keep the queue small.
    /// </summary>
    public TimeSpan FlushInterval
    {
        get => _flushInterval;
        set => _flushInterval = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value), value, "The telemetry flush interval must be positive.");
    }
}
