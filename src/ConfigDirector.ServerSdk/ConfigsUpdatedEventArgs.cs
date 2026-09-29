namespace ConfigDirector;

/// <summary>Raised when new config state arrives from the server.</summary>
public sealed class ConfigsUpdatedEventArgs : EventArgs
{
    internal ConfigsUpdatedEventArgs(IReadOnlyList<string> keys, IReadOnlyList<string> removedKeys)
    {
        Keys = keys;
        RemovedKeys = removedKeys;
    }

    /// <summary>The keys the update carried, sorted.</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>
    /// The keys a full update no longer carried, so the client stopped serving them, sorted. Empty
    /// when nothing was removed, and always empty for a delta update.
    /// </summary>
    public IReadOnlyList<string> RemovedKeys { get; }
}
