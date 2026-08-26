namespace ConfigDirector;

/// <summary>Raised when new config state arrives from the server.</summary>
public sealed class ConfigsUpdatedEventArgs : EventArgs
{
    internal ConfigsUpdatedEventArgs(IReadOnlyList<string> keys) => Keys = keys;

    /// <summary>The keys the update carried, sorted.</summary>
    public IReadOnlyList<string> Keys { get; }
}
