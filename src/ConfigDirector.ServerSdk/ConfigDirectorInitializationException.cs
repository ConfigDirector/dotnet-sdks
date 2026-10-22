namespace ConfigDirector;

/// <summary>
/// The client could not load the values it was given to serve in
/// <see cref="ConnectionMode.Offline"/>, or was given them under another connection mode.
/// </summary>
public sealed class ConfigDirectorInitializationException : ConfigDirectorException
{
    /// <summary>Builds an initialization failure with no underlying cause.</summary>
    /// <param name="message">What went wrong.</param>
    public ConfigDirectorInitializationException(string message)
        : base(message)
    {
    }

    /// <summary>Builds an initialization failure that wraps the failure underneath it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The failure underneath, such as the one that ended reading a file.</param>
    public ConfigDirectorInitializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Builds an initialization failure with no message.</summary>
    public ConfigDirectorInitializationException()
    {
    }
}
