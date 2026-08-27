using ConfigDirector.Evaluation;
using ConfigDirector.Transport;
using ConfigDirector.Value;
using Microsoft.Extensions.Logging;

namespace ConfigDirector;

/// <summary>
/// The default <see cref="IConfigDirectorClient"/>.
/// </summary>
/// <remarks>
/// <code>
/// await using var client = new ConfigDirectorClient(serverSdkKey);
/// await client.InitializeAsync();
///
/// var enabled = client.GetValue("temporary-feature-flag", false, new Context { Id = "user-1" });
/// </code>
/// </remarks>
public sealed class ConfigDirectorClient : IConfigDirectorClient
{
    private static readonly IReadOnlyDictionary<string, ConfigState> EmptyState =
        new Dictionary<string, ConfigState>(StringComparer.Ordinal);

    private readonly object _watchLock = new();
    private readonly Dictionary<string, List<Watcher>> _watchers = new(StringComparer.Ordinal);
    private readonly ILogger<ConfigDirectorClient> _logger;
    private readonly Metadata? _metadata;
    private readonly TimeSpan _timeout;
    private readonly ConfigEvaluator _evaluator;
    private readonly ITransport _transport;

    // Null until the first bundle arrives, which is what separates "not ready" from "ready but the
    // server does not know this key". Only ever swapped, never edited in place, so a read on the
    // path every GetValue takes is a volatile read and a lookup.
    private volatile IReadOnlyDictionary<string, Config>? _configs;
    private volatile bool _closed;

    /// <summary>
    /// Builds a client that has not connected yet.
    /// </summary>
    /// <param name="serverSdkKey">A secret; do not commit it to source control.</param>
    /// <param name="options">The settings to build with, or null for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverSdkKey"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverSdkKey"/> is empty or whitespace.</exception>
    public ConfigDirectorClient(string serverSdkKey, ConfigDirectorClientOptions? options = null)
    {
        if (serverSdkKey is null)
        {
            throw new ArgumentNullException(nameof(serverSdkKey));
        }

        if (string.IsNullOrWhiteSpace(serverSdkKey))
        {
            throw new ArgumentException(
                "The client cannot be built without a server SDK key.", nameof(serverSdkKey));
        }

        var settings = options ?? new ConfigDirectorClientOptions();
        _logger = settings.LoggerFactory.CreateLogger<ConfigDirectorClient>();
        _metadata = settings.Metadata;
        _timeout = settings.Connection.Timeout;
        _evaluator = new ConfigEvaluator(settings.LoggerFactory.CreateLogger<ConfigEvaluator>());

        var connection = settings.Connection;
        _transport = TransportFactory.Create(
            connection.Mode,
            new TransportOptions(
                serverSdkKey,
                connection.Url ?? Transports.DefaultBaseUrl,
                OnBundle,
                settings.LoggerFactory)
            {
                Metadata = settings.Metadata,
                PollingInterval = connection.PollingInterval,
                RequestTimeout = connection.Timeout,
            });
    }

    /// <inheritdoc/>
    public event EventHandler<ClientReadyEventArgs>? ClientReady;

    /// <inheritdoc/>
    public event EventHandler<ConfigsUpdatedEventArgs>? ConfigsUpdated;

    /// <inheritdoc/>
    public event EventHandler<ConfigEvaluatedEventArgs>? ConfigEvaluated;

    /// <inheritdoc/>
    public bool IsReady => Configs is not null;

    /// <inheritdoc/>
    public bool IsClosed => _closed;

    // Disposal is what makes config state unreachable, so every reader goes through here rather
    // than through the field: a bundle still in flight when the client closes cannot bring it back.
    private IReadOnlyDictionary<string, Config>? Configs => _closed ? null : _configs;

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfClosed();

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(_timeout);

        try
        {
            await _transport.ConnectAsync(attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Log.InitializationTimedOut(_logger, _timeout, null);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Reported through IsReady rather than by throwing. An application that cannot reach
            // ConfigDirector should still start and serve its defaults, and every SDK that fronts
            // this service behaves the same way.
            Log.InitializationFailed(_logger, error);
        }
    }

    /// <inheritdoc/>
    public T GetValue<T>(string configKey, T defaultValue, Context? context = null)
        where T : notnull
    {
        ValidateKey(configKey);
        if (defaultValue is null)
        {
            throw new ArgumentNullException(nameof(defaultValue));
        }

        var configs = Configs;
        Config? definition = null;
        configs?.TryGetValue(configKey, out definition);

        return Evaluate(configKey, definition, defaultValue, context);
    }

    // Shared by the getter and by a watch being notified: for a watch the definition comes from
    // the update that carried it, so the two only differ in where the definition was found.
    private T Evaluate<T>(string configKey, Config? definition, T defaultValue, Context? context)
    {
        if (definition is null)
        {
            Log.NoConfigState(_logger, configKey, null);
            var reason = IsReady ? EvaluationReason.ConfigStateMissing : EvaluationReason.ClientNotReady;
            Report(configKey, defaultValue, true, reason, null, context);
            return defaultValue;
        }

        var state = _evaluator.Evaluate(definition, context, _metadata);
        var result = ValueParser.Parse(state, defaultValue);
        Report(configKey, result.Value, result.UsedDefault, result.Reason, result.ValueId, context);
        return result.Value;
    }

    // Generic all the way through, so nothing is boxed for an evaluation nobody is listening to.
    private void Report<T>(
        string configKey,
        T value,
        bool isDefault,
        EvaluationReason reason,
        string? valueId,
        Context? context)
    {
        var handlers = ConfigEvaluated;
        if (handlers is null)
        {
            return;
        }

        var evaluation = new ConfigEvaluation
        {
            Key = configKey,
            Value = value!,
            IsDefault = isDefault,
            Reason = reason,
            ValueId = valueId,
            Context = context,
        };

        Raise(handlers, new ConfigEvaluatedEventArgs(evaluation), nameof(ConfigEvaluated));
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, ConfigState> GetAllConfigs(
        Context? context = null,
        IEnumerable<string>? configKeys = null)
    {
        var configs = Configs;
        if (configs is null)
        {
            return EmptyState;
        }

        // A set, so filtering stays linear in the number of configs rather than scanning the
        // requested keys once per config. Walking the definitions rather than the request also
        // collapses a key asked for twice.
        var requested = configKeys is null
            ? null
            : new HashSet<string>(configKeys, StringComparer.Ordinal);

        var evaluated = new Dictionary<string, ConfigState>(StringComparer.Ordinal);
        foreach (var entry in configs)
        {
            if (requested is null || requested.Contains(entry.Key))
            {
                evaluated[entry.Key] = _evaluator.Evaluate(entry.Value, context, _metadata);
            }
        }

        return evaluated;
    }

    /// <inheritdoc/>
    public IDisposable Watch<T>(string configKey, T defaultValue, Action<T> onChange, Context? context = null)
        where T : notnull
    {
        ValidateKey(configKey);
        if (defaultValue is null)
        {
            throw new ArgumentNullException(nameof(defaultValue));
        }

        if (onChange is null)
        {
            throw new ArgumentNullException(nameof(onChange));
        }

        var watcher = new Watcher(definition =>
            onChange(Evaluate(configKey, definition, defaultValue, context)));

        lock (_watchLock)
        {
            if (!_watchers.TryGetValue(configKey, out var entries))
            {
                entries = [];
                _watchers[configKey] = entries;
            }

            entries.Add(watcher);
        }

        return new Cancellation(() => Remove(configKey, watcher));
    }

    /// <inheritdoc/>
    public void Unwatch(string configKey)
    {
        ValidateKey(configKey);
        lock (_watchLock)
        {
            _watchers.Remove(configKey);
        }
    }

    /// <inheritdoc/>
    public void UnwatchAll()
    {
        lock (_watchLock)
        {
            _watchers.Clear();
        }
    }

    /// <summary>
    /// Closes the connection, and cancels every watch and handler. Disposing twice is harmless.
    /// </summary>
    public void Dispose()
    {
        if (Close())
        {
            _transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Closes the connection. Disposing twice is harmless.
    /// </summary>
    /// <returns>A task that completes once the connection has been released.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Close())
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private bool Close()
    {
        if (_closed)
        {
            return false;
        }

        _closed = true;
        _configs = null;
        UnwatchAll();
        ClientReady = null;
        ConfigsUpdated = null;
        ConfigEvaluated = null;
        Log.Closed(_logger, null);
        return true;
    }

    private void OnBundle(ConfigBundle bundle)
    {
        if (_closed)
        {
            return;
        }

        var firstBundle = _configs is null;
        _configs = bundle.Configs;
        Log.ConfigStateUpdated(_logger, bundle.Configs.Count, null);

        var keys = bundle.Configs.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        Raise(ConfigsUpdated, new ConfigsUpdatedEventArgs(keys), nameof(ConfigsUpdated));
        NotifyWatchers(bundle.Configs);

        if (firstBundle)
        {
            Raise(ClientReady, new ClientReadyEventArgs(), nameof(ClientReady));
        }
    }

    // Notified from the update rather than from the merged state: a watch only fires for a key the
    // update carried, and for those two the definition is the same one.
    private void NotifyWatchers(IReadOnlyDictionary<string, Config> updated)
    {
        foreach (var entry in updated)
        {
            Watcher[] entries;
            lock (_watchLock)
            {
                if (!_watchers.TryGetValue(entry.Key, out var registered))
                {
                    continue;
                }

                // Copied, so a watch cancelling itself cannot edit the list being walked.
                entries = [.. registered];
            }

            foreach (var watcher in entries)
            {
                try
                {
                    watcher.Notify(entry.Value);
                }
                catch (Exception error)
                {
                    // One faulty watch must not cost the others their update, nor take down the
                    // thread the update arrived on.
                    Log.WatchThrew(_logger, entry.Key, error);
                }
            }
        }
    }

    private void Remove(string configKey, Watcher watcher)
    {
        lock (_watchLock)
        {
            if (_watchers.TryGetValue(configKey, out var entries) && entries.Remove(watcher) && entries.Count == 0)
            {
                _watchers.Remove(configKey);
            }
        }
    }

    private void Raise<TArgs>(EventHandler<TArgs>? handlers, TArgs args, string name)
        where TArgs : EventArgs
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<TArgs>)handler)(this, args);
            }
            catch (Exception error)
            {
                // A faulty handler must not break the caller, nor the handlers registered after it.
                Log.HandlerThrew(_logger, name, error);
            }
        }
    }

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw new ObjectDisposedException(nameof(ConfigDirectorClient));
        }
    }

    private static void ValidateKey(string configKey)
    {
        if (configKey is null)
        {
            throw new ArgumentNullException(nameof(configKey));
        }

        if (string.IsNullOrWhiteSpace(configKey))
        {
            throw new ArgumentException("The config key must not be empty.", nameof(configKey));
        }
    }

    // Identity, not equality: two identical watches stay distinct, so cancelling one leaves the
    // other in place.
    private sealed class Watcher(Action<Config> notify)
    {
        internal void Notify(Config definition) => notify(definition);
    }

    private sealed class Cancellation(Action cancel) : IDisposable
    {
        public void Dispose() => cancel();
    }

    private static class Log
    {
        internal static readonly Action<ILogger, TimeSpan, Exception?> InitializationTimedOut =
            LoggerMessage.Define<TimeSpan>(
                LogLevel.Warning,
                new EventId(1, "InitializationTimedOut"),
                "Timed out waiting for initialization after {Timeout}. Configs will return their "
                    + "default value until config state arrives.");

        internal static readonly Action<ILogger, Exception?> InitializationFailed =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(8, "InitializationFailed"),
                "Initialization failed. Configs will return their default value until config state "
                    + "arrives.");

        internal static readonly Action<ILogger, string, Exception?> NoConfigState =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(2, "NoConfigState"),
                "No config state was found for {ConfigKey}, returning the default value.");

        internal static readonly Action<ILogger, int, Exception?> ConfigStateUpdated =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                new EventId(3, "ConfigStateUpdated"),
                "Config state updated with {ConfigCount} key(s).");

        internal static readonly Action<ILogger, string, Exception?> HandlerThrew =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(5, "HandlerThrew"),
                "A handler for {EventName} threw.");

        internal static readonly Action<ILogger, string, Exception?> WatchThrew =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(6, "WatchThrew"),
                "A watch on {ConfigKey} threw.");

        internal static readonly Action<ILogger, Exception?> Closed =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(4, "Closed"),
                "The client has been closed.");
    }
}
