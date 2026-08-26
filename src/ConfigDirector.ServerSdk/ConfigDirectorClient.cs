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
/// var enabled = client.GetValue("new-checkout", false, new Context { Id = "user-1" });
/// </code>
/// </remarks>
public sealed class ConfigDirectorClient : IConfigDirectorClient
{
    private static readonly IReadOnlyDictionary<string, ConfigState> EmptyState =
        new Dictionary<string, ConfigState>(StringComparer.Ordinal);

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
        : this(serverSdkKey, options, onBundle => new StubTransport(onBundle))
    {
    }

    internal ConfigDirectorClient(
        string serverSdkKey,
        ConfigDirectorClientOptions? options,
        Func<Action<ConfigBundle>, ITransport> createTransport)
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
        _transport = createTransport(OnBundle);
    }

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
        if (configs is null || !configs.TryGetValue(configKey, out var definition))
        {
            Log.NoConfigState(_logger, configKey, null);
            return defaultValue;
        }

        var state = _evaluator.Evaluate(definition, context, _metadata);
        return ValueParser.Parse(state, defaultValue).Value;
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

    /// <summary>
    /// Closes the connection. Disposing twice is harmless.
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
        Log.Closed(_logger, null);
        return true;
    }

    private void OnBundle(ConfigBundle bundle)
    {
        _configs = bundle.Configs;
        Log.ConfigStateUpdated(_logger, bundle.Configs.Count, null);
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

    private static class Log
    {
        internal static readonly Action<ILogger, TimeSpan, Exception?> InitializationTimedOut =
            LoggerMessage.Define<TimeSpan>(
                LogLevel.Warning,
                new EventId(1, "InitializationTimedOut"),
                "Timed out waiting for initialization after {Timeout}. Configs will return their "
                    + "default value until config state arrives.");

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

        internal static readonly Action<ILogger, Exception?> Closed =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(4, "Closed"),
                "The client has been closed.");
    }
}
