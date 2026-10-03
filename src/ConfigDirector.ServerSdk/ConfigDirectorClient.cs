using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ConfigDirector.Evaluation;
using ConfigDirector.Telemetry;
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
    private readonly ITelemetryCollector _telemetry;

    // Null until the first bundle arrives, which is what separates "not ready" from "ready but the
    // server does not know this key". Only ever swapped, never edited in place, so a read on the
    // path every GetValue takes is a volatile read and a lookup.
    private volatile ServedDefinitions? _served;
    private volatile bool _closed;
    private int _closing;

    /// <summary>
    /// Builds a client that has not connected yet.
    /// </summary>
    /// <param name="serverSdkKey">A secret; do not commit it to source control.</param>
    /// <param name="options">The settings to build with, or null for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverSdkKey"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverSdkKey"/> is empty or whitespace.</exception>
    public ConfigDirectorClient(string serverSdkKey, ConfigDirectorClientOptions? options = null)
        : this(serverSdkKey, options, SdkIdentity.ServerSdk)
    {
    }

    internal ConfigDirectorClient(string serverSdkKey, ConfigDirectorClientOptions? options, SdkIdentity identity)
        : this(serverSdkKey, options, identity, TransportFactory.Create)
    {
    }

    internal ConfigDirectorClient(
        string serverSdkKey,
        ConfigDirectorClientOptions? options,
        SdkIdentity identity,
        Func<ConnectionMode, TransportOptions, ITransport> buildTransport)
        : this(serverSdkKey, options, identity, buildTransport, telemetryOptions => new TelemetryCollector(telemetryOptions))
    {
    }

    internal ConfigDirectorClient(
        string serverSdkKey,
        ConfigDirectorClientOptions? options,
        SdkIdentity identity,
        Func<ConnectionMode, TransportOptions, ITransport> buildTransport,
        Func<TelemetryCollectorOptions, ITelemetryCollector> buildTelemetry)
    {
        if (identity is null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

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
        var baseUrl = connection.Url ?? Transports.DefaultBaseUrl;

        _telemetry = buildTelemetry(
            new TelemetryCollectorOptions(serverSdkKey, baseUrl, identity, settings.LoggerFactory)
            {
                Metadata = settings.Metadata,
                EventQueueLimit = settings.Telemetry.EventQueueLimit,
                FlushInterval = settings.Telemetry.FlushInterval,
            });

        _transport = buildTransport(
            connection.Mode,
            new TransportOptions(serverSdkKey, baseUrl, OnBundle, identity, settings.LoggerFactory)
            {
                Metadata = settings.Metadata,
                PollingInterval = ResolvePollingInterval(connection),
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
    public bool IsReady => Served is not null;

    /// <inheritdoc/>
    public bool IsClosed => _closed;

    // Disposal is what makes config state unreachable, so every reader goes through here rather
    // than through the field: a bundle still in flight when the client closes cannot bring it back.
    private ServedDefinitions? Served => _closed ? null : _served;

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
    public int GetValue(string configKey, int defaultValue, Context? context = null)
        => Read(configKey, defaultValue, context, ValueParser.Parse);

    /// <inheritdoc/>
    public long GetValue(string configKey, long defaultValue, Context? context = null)
        => Read(configKey, defaultValue, context, ValueParser.Parse);

    /// <inheritdoc/>
    public double GetValue(string configKey, double defaultValue, Context? context = null)
        => Read(configKey, defaultValue, context, ValueParser.Parse);

    /// <inheritdoc/>
    public float GetValue(string configKey, float defaultValue, Context? context = null)
        => Read(configKey, defaultValue, context, ValueParser.Parse);

    /// <inheritdoc/>
    public decimal GetValue(string configKey, decimal defaultValue, Context? context = null)
        => Read(configKey, defaultValue, context, ValueParser.Parse);

    /// <inheritdoc/>
    public bool GetValue(string configKey, bool defaultValue, Context? context = null)
        => Read(configKey, defaultValue, context, ValueParser.Parse);

    /// <inheritdoc/>
    public string GetValue(string configKey, string defaultValue, Context? context = null)
        => Read(configKey, defaultValue, context, ValueParser.Parse);

    /// <inheritdoc/>
    public JsonElement GetValue(string configKey, JsonElement defaultValue, Context? context = null)
        => Read(configKey, defaultValue, context, ValueParser.Parse);

    /// <inheritdoc/>
    [RequiresUnreferencedCode(Reflective.BindingNeedsReflection)]
    [RequiresDynamicCode(Reflective.BindingNeedsReflection)]
    public T GetJsonValue<T>(string configKey, T defaultValue, Context? context = null)
        where T : notnull =>
        Read(configKey, defaultValue, context, ValueParser.Bind);

    private T Read<T>(string configKey, T defaultValue, Context? context, ValueReader<T> parse)
    {
        ValidateKey(configKey);
        if (defaultValue is null)
        {
            throw new ArgumentNullException(nameof(defaultValue));
        }

        var served = Served;
        Config? definition = null;
        served?.Configs.TryGetValue(configKey, out definition);

        return Evaluate(configKey, definition, defaultValue, context, parse, served?.Segments ?? ConfigEvaluator.NoSegments);
    }

    // Shared by the getter and by a watch being notified: for a watch the definition comes from
    // the update that carried it, so the two only differ in where the definition was found.
    private T Evaluate<T>(
        string configKey,
        Config? definition,
        T defaultValue,
        Context? context,
        ValueReader<T> parse,
        IReadOnlyDictionary<string, Segment> segments)
    {
        if (definition is null)
        {
            Log.NoConfigState(_logger, configKey, null);
            var reason = IsReady ? EvaluationReason.ConfigStateMissing : EvaluationReason.ClientNotReady;
            Report(configKey, defaultValue, defaultValue, true, reason, null, null, context);
            return defaultValue;
        }

        var state = _evaluator.Evaluate(definition, context, _metadata, segments);
        var result = parse(state, defaultValue);
        Report(
            configKey,
            defaultValue,
            result.Value,
            result.UsedDefault,
            result.Reason,
            result.ValueId,
            state.Type,
            context);
        return result.Value;
    }

    // Generic all the way through, so nothing is boxed for an evaluation nobody is listening to.
    private void Report<T>(
        string configKey,
        T defaultValue,
        T value,
        bool isDefault,
        EvaluationReason reason,
        string? valueId,
        ConfigType? configType,
        Context? context)
    {
        _telemetry.Record(configKey, defaultValue, value, isDefault, reason, context, configType, valueId);

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
        var served = Served;
        var configs = served?.Configs;
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
                evaluated[entry.Key] = _evaluator.Evaluate(entry.Value, context, _metadata, served!.Segments);
            }
        }

        return evaluated;
    }

    /// <inheritdoc/>
    public IDisposable Watch(string configKey, int defaultValue, Action<int> onChange, Context? context = null)
        => Observe(configKey, defaultValue, onChange, context, ValueParser.Parse);

    /// <inheritdoc/>
    public IDisposable Watch(string configKey, long defaultValue, Action<long> onChange, Context? context = null)
        => Observe(configKey, defaultValue, onChange, context, ValueParser.Parse);

    /// <inheritdoc/>
    public IDisposable Watch(string configKey, double defaultValue, Action<double> onChange, Context? context = null)
        => Observe(configKey, defaultValue, onChange, context, ValueParser.Parse);

    /// <inheritdoc/>
    public IDisposable Watch(string configKey, float defaultValue, Action<float> onChange, Context? context = null)
        => Observe(configKey, defaultValue, onChange, context, ValueParser.Parse);

    /// <inheritdoc/>
    public IDisposable Watch(string configKey, decimal defaultValue, Action<decimal> onChange, Context? context = null)
        => Observe(configKey, defaultValue, onChange, context, ValueParser.Parse);

    /// <inheritdoc/>
    public IDisposable Watch(string configKey, bool defaultValue, Action<bool> onChange, Context? context = null)
        => Observe(configKey, defaultValue, onChange, context, ValueParser.Parse);

    /// <inheritdoc/>
    public IDisposable Watch(string configKey, string defaultValue, Action<string> onChange, Context? context = null)
        => Observe(configKey, defaultValue, onChange, context, ValueParser.Parse);

    /// <inheritdoc/>
    public IDisposable Watch(string configKey, JsonElement defaultValue, Action<JsonElement> onChange, Context? context = null)
        => Observe(configKey, defaultValue, onChange, context, ValueParser.Parse);

    /// <inheritdoc/>
    [RequiresUnreferencedCode(Reflective.BindingNeedsReflection)]
    [RequiresDynamicCode(Reflective.BindingNeedsReflection)]
    public IDisposable WatchJson<T>(string configKey, T defaultValue, Action<T> onChange, Context? context = null)
        where T : notnull =>
        Observe(configKey, defaultValue, onChange, context, ValueParser.Bind);

    private Cancellation Observe<T>(
        string configKey,
        T defaultValue,
        Action<T> onChange,
        Context? context,
        ValueReader<T> parse)
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

        var watcher = new Watcher((definition, segments) =>
            onChange(Evaluate(configKey, definition, defaultValue, context, parse, segments)));

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
            ReleaseAsync().AsTask().GetAwaiter().GetResult();
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
            await ReleaseAsync().ConfigureAwait(false);
        }
    }

    // Shared by both disposal paths: a container that disposes synchronously has to release the
    // same things, telemetry's final report included.
    private async ValueTask ReleaseAsync()
    {
        await _transport.DisposeAsync().ConfigureAwait(false);

        // Last, so that everything the application evaluated on its way down is reported.
        await _telemetry.DisposeAsync().ConfigureAwait(false);
    }

    private bool Close()
    {
        // Checking the flag and setting it has to be one step: two callers racing to dispose would
        // otherwise both get through and tear the same connection down twice.
        if (Interlocked.Exchange(ref _closing, 1) == 1)
        {
            return false;
        }

        _closed = true;
        _served = null;
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

        var current = _served;
        var firstBundle = current is null;

        // A delta carries only what changed, so it is merged into what is already held. A full
        // bundle is the entire config state and replaces it, which is also how a config that was
        // deleted stops being served.
        var isDelta = bundle.Kind == BundleKind.Delta && current is not null;
        var served = isDelta
            ? new ServedDefinitions(Merge(current!.Configs, bundle.Configs), Merge(current.Segments, bundle.Segments))
            : new ServedDefinitions(bundle.Configs, bundle.Segments);
        _served = served;
        var removedKeys = isDelta || current is null ? [] : KeysAbsentFrom(current.Configs, bundle.Configs);

        Log.ConfigStateUpdated(_logger, bundle.Configs.Count, removedKeys.Length, null);

        var keys = bundle.Configs.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        Raise(ConfigsUpdated, new ConfigsUpdatedEventArgs(keys, removedKeys), nameof(ConfigsUpdated));
        NotifyWatchers(bundle.Configs, removedKeys, served.Segments);

        if (firstBundle)
        {
            Raise(ClientReady, new ClientReadyEventArgs(), nameof(ClientReady));
        }
    }

    private static Dictionary<string, T> Merge<T>(
        IReadOnlyDictionary<string, T> current,
        IReadOnlyDictionary<string, T> update)
    {
        // Copied rather than edited in place, so a reader already walking the config state is not
        // overtaken by an update landing underneath it.
        var merged = new Dictionary<string, T>(current.Count + update.Count, StringComparer.Ordinal);
        foreach (var entry in current)
        {
            merged[entry.Key] = entry.Value;
        }

        foreach (var entry in update)
        {
            merged[entry.Key] = entry.Value;
        }

        return merged;
    }

    private static string[] KeysAbsentFrom(
        IReadOnlyDictionary<string, Config> previous,
        IReadOnlyDictionary<string, Config> updated) =>
        previous.Keys
            .Where(key => !updated.ContainsKey(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

    // Notified from the update rather than from the merged state: for a key the update carried the
    // two hold the same definition, and a removed key has none, so its watches get the default.
    private void NotifyWatchers(
        IReadOnlyDictionary<string, Config> updated,
        string[] removedKeys,
        IReadOnlyDictionary<string, Segment> segments)
    {
        foreach (var entry in updated)
        {
            NotifyWatchers(entry.Key, entry.Value, segments);
        }

        foreach (var key in removedKeys)
        {
            NotifyWatchers(key, null, segments);
        }
    }

    private void NotifyWatchers(string key, Config? definition, IReadOnlyDictionary<string, Segment> segments)
    {
        Watcher[] entries;
        lock (_watchLock)
        {
            if (!_watchers.TryGetValue(key, out var registered))
            {
                return;
            }

            // Copied, so a watch cancelling itself cannot edit the list being walked.
            entries = [.. registered];
        }

        foreach (var watcher in entries)
        {
            try
            {
                watcher.Notify(definition, segments);
            }
            catch (Exception error)
            {
                // One faulty watch must not cost the others their update, nor take down the
                // thread the update arrived on.
                Log.WatchThrew(_logger, key, error);
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
    private sealed record ServedDefinitions(IReadOnlyDictionary<string, Config> Configs, IReadOnlyDictionary<string, Segment> Segments);

    private sealed class Watcher(Action<Config?, IReadOnlyDictionary<string, Segment>> notify)
    {
        internal void Notify(Config? definition, IReadOnlyDictionary<string, Segment> segments) =>
            notify(definition, segments);
    }

    private sealed class Cancellation(Action cancel) : IDisposable
    {
        public void Dispose() => cancel();
    }

    private TimeSpan ResolvePollingInterval(ConnectionOptions connection)
    {
        var configuredInterval = connection.PollingInterval;
        if (connection.Mode != ConnectionMode.Polling
            || configuredInterval >= ConnectionOptions.MinPollingInterval)
        {
            return configuredInterval;
        }

        Log.PollingIntervalRaised(
            _logger, configuredInterval, ConnectionOptions.MinPollingInterval, ConnectionOptions.MinPollingInterval, null);
        return ConnectionOptions.MinPollingInterval;
    }

    private static class Log
    {
        internal static readonly Action<ILogger, TimeSpan, TimeSpan, TimeSpan, Exception?> PollingIntervalRaised =
            LoggerMessage.Define<TimeSpan, TimeSpan, TimeSpan>(
                LogLevel.Warning,
                new EventId(7, "PollingIntervalRaised"),
                "PollingInterval of {ConfiguredInterval} is below the minimum of {MinPollingInterval}. "
                    + "Using {RaisedInterval}.");

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

        internal static readonly Action<ILogger, int, int, Exception?> ConfigStateUpdated =
            LoggerMessage.Define<int, int>(
                LogLevel.Debug,
                new EventId(3, "ConfigStateUpdated"),
                "Config state updated with {ConfigCount} key(s), {RemovedCount} removed.");

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
