namespace ConfigDirector;

/// <summary>The SDK could not retrieve config state from ConfigDirector.</summary>
public sealed class ConfigDirectorConnectionException : ConfigDirectorException
{
    /// <summary>Builds a connection failure carrying the status the server answered with.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="status">The HTTP status, or null when no response arrived.</param>
    public ConfigDirectorConnectionException(string message, int? status = null)
        : base(message) => Status = status;

    /// <summary>Builds a connection failure that wraps the failure underneath it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The failure underneath.</param>
    public ConfigDirectorConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Builds a connection failure with no message.</summary>
    public ConfigDirectorConnectionException()
    {
    }

    /// <summary>The HTTP status the server answered with, or null when no response arrived.</summary>
    public int? Status { get; }
}
