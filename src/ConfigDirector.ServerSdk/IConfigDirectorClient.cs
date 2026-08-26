namespace ConfigDirector;

/// <summary>
/// Reads configs and feature flags from ConfigDirector.
/// </summary>
/// <remarks>
/// <para>
/// One client per application is enough: it is safe to share across threads, and holds the
/// connection that disposing it releases. Building one makes no network calls — call
/// <see cref="InitializeAsync"/> once during startup to connect.
/// </para>
/// <para>
/// <see cref="GetValue{T}"/> takes a default and returns it rather than throwing: a config the SDK
/// has never heard of, an unreachable server, or a value that will not coerce to the requested type
/// all produce the default.
/// </para>
/// </remarks>
public interface IConfigDirectorClient : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Raised once, when the first config state arrives. It reports the transition, so a handler
    /// added to an already-ready client is never called — check <see cref="IsReady"/> for that.
    /// </summary>
    event EventHandler<ClientReadyEventArgs>? ClientReady;

    /// <summary>
    /// Raised every time new config state arrives. Handlers run on the thread the update arrived
    /// on, so one that blocks delays later updates.
    /// </summary>
    event EventHandler<ConfigsUpdatedEventArgs>? ConfigsUpdated;

    /// <summary>
    /// Raised every time a config is evaluated, including evaluations that returned the caller's
    /// default. Handlers run on the thread that asked, so one that blocks delays the call that
    /// triggered it.
    /// </summary>
    event EventHandler<ConfigEvaluatedEventArgs>? ConfigEvaluated;

    /// <summary>Whether config state has arrived. Until it has, every getter returns its default.</summary>
    bool IsReady { get; }

    /// <summary>Whether the client has been disposed. A disposed client cannot be reopened.</summary>
    bool IsClosed { get; }

    /// <summary>
    /// Connects and waits for the first config state, bounded by
    /// <see cref="ConnectionOptions.Timeout"/>.
    /// </summary>
    /// <param name="cancellationToken">Abandons the wait.</param>
    /// <returns>A task that completes once the client is ready or the timeout elapses.</returns>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates <paramref name="configKey"/> as <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// The type comes from <paramref name="defaultValue"/>, not from how the config was declared in
    /// the dashboard. <see langword="string"/>, <see langword="bool"/>, and the numeric types are
    /// read from the value directly; anything else is deserialised from it as JSON, so a config
    /// holding a JSON object can be read straight into a type of your own.
    /// </remarks>
    /// <typeparam name="T">The type of the default, and of the value returned.</typeparam>
    /// <param name="configKey">The config to read.</param>
    /// <param name="defaultValue">
    /// Returned when the config is missing, unreachable, or will not coerce to
    /// <typeparamref name="T"/>.
    /// </param>
    /// <param name="context">Evaluated against targeting rules; may be null.</param>
    /// <returns>The evaluated value, or <paramref name="defaultValue"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="configKey"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="configKey"/> or <paramref name="defaultValue"/> is null.
    /// </exception>
    T GetValue<T>(string configKey, T defaultValue, Context? context = null)
        where T : notnull;

    /// <summary>
    /// Every config the SDK currently holds, evaluated, before type parsing.
    /// </summary>
    /// <remarks>
    /// Intended for handing state to a client SDK to hydrate with. It records no telemetry, since
    /// the SDK that receives the state reports its own evaluations.
    /// </remarks>
    /// <param name="context">Evaluated against targeting rules; may be null.</param>
    /// <param name="configKeys">The keys to include, or null for every key the SDK holds.</param>
    /// <returns>
    /// The evaluated state by key, or an empty dictionary before the first config state arrives.
    /// </returns>
    IReadOnlyDictionary<string, ConfigState> GetAllConfigs(
        Context? context = null,
        IEnumerable<string>? configKeys = null);

    /// <summary>
    /// Calls <paramref name="onChange"/> with the newly evaluated value whenever an update carries
    /// <paramref name="configKey"/>.
    /// </summary>
    /// <remarks>
    /// Handlers run on the thread the update arrived on, so one that blocks delays later updates.
    /// Register the watch before <see cref="InitializeAsync"/> to be called for the first config
    /// state as well.
    /// </remarks>
    /// <typeparam name="T">The type of the default, and of the value passed to <paramref name="onChange"/>.</typeparam>
    /// <param name="configKey">The config to watch.</param>
    /// <param name="defaultValue">Used when the updated config will not coerce to <typeparamref name="T"/>.</param>
    /// <param name="onChange">Receives the newly evaluated value.</param>
    /// <param name="context">Evaluated against targeting rules; may be null.</param>
    /// <returns>A handle that cancels this watch. Disposing it twice is harmless.</returns>
    /// <exception cref="ArgumentException"><paramref name="configKey"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="context"/> is null.</exception>
    IDisposable Watch<T>(string configKey, T defaultValue, Action<T> onChange, Context? context = null)
        where T : notnull;

    /// <summary>Cancels every watch on one config.</summary>
    /// <param name="configKey">The config to stop watching.</param>
    void Unwatch(string configKey);

    /// <summary>Cancels every watch on every config.</summary>
    void UnwatchAll();
}
