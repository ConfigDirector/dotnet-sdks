namespace ConfigDirector.Transport;

// The payload was not a config bundle at all.
internal sealed class BundleFormatException : Exception
{
    internal BundleFormatException(string message)
        : base(message)
    {
    }

    internal BundleFormatException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

// Well-formed JSON, but carrying no configs. A streaming connection sees these whenever the
// server sends something alongside config updates, such as a heartbeat.
internal sealed class NotAConfigBundleException : Exception
{
    internal NotAConfigBundleException(string message)
        : base(message)
    {
    }
}
