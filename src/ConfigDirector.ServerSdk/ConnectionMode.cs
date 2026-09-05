namespace ConfigDirector;

/// <summary>How the client keeps its config state current.</summary>
public enum ConnectionMode
{
    /// <summary>The connection stays open and receives updates as config state changes.</summary>
    Streaming = 0,

    /// <summary>
    /// Config state is fetched during initialization, then re-fetched every
    /// <see cref="ConnectionOptions.PollingInterval"/>.
    /// </summary>
    Polling,
}
