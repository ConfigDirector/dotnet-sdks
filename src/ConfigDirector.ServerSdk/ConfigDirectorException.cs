namespace ConfigDirector;

/// <summary>Base class for failures the SDK reports about its own operation.</summary>
public class ConfigDirectorException : Exception
{
    /// <summary>Builds an exception with no underlying cause.</summary>
    /// <param name="message">What went wrong.</param>
    public ConfigDirectorException(string message)
        : base(message)
    {
    }

    /// <summary>Builds an exception that wraps the failure underneath it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The failure underneath, such as the one that ended a request.</param>
    public ConfigDirectorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Builds an exception with no message.</summary>
    public ConfigDirectorException()
    {
    }
}
