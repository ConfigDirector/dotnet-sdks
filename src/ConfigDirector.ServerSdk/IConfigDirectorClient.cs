using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

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
/// Every <c>GetValue</c> overload takes a default and returns it rather than throwing: a config the
/// SDK has never heard of, an unreachable server, or a value that will not read as the type asked
/// for all produce the default. There is one overload per type the SDK can read exactly, so a type
/// it cannot fill is a compile error rather than a surprise at runtime.
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

    /// <summary>Evaluates <paramref name="configKey"/> and reads its value as an integer.</summary>
    /// <remarks>
    /// The type read is the overload you call, not how the config was declared in the dashboard: a
    /// config holding <c>"true"</c> read through this overload yields
    /// <paramref name="defaultValue"/>, never a number coerced from something that is not one. A
    /// whole number the server wrote as <c>26.0</c> or <c>2.6e1</c> still reads as <c>26</c>.
    /// </remarks>
    /// <param name="configKey">The config to read.</param>
    /// <param name="defaultValue">
    /// Returned when the config is missing, unreachable, or its value will not read as this type.
    /// </param>
    /// <param name="context">Evaluated against targeting rules; may be null.</param>
    /// <returns>The evaluated value, or <paramref name="defaultValue"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="configKey"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configKey"/> is null.</exception>
    int GetValue(string configKey, int defaultValue, Context? context = null);

    /// <summary>Evaluates <paramref name="configKey"/> and reads its value as a long.</summary>
    /// <inheritdoc cref="GetValue(string, int, Context)"/>
    long GetValue(string configKey, long defaultValue, Context? context = null);

    /// <summary>Evaluates <paramref name="configKey"/> and reads its value as a double.</summary>
    /// <inheritdoc cref="GetValue(string, int, Context)"/>
    double GetValue(string configKey, double defaultValue, Context? context = null);

    /// <summary>Evaluates <paramref name="configKey"/> and reads its value as a float.</summary>
    /// <remarks>
    /// A value the server can hold but a <see langword="float"/> cannot, such as <c>1e300</c>,
    /// yields <paramref name="defaultValue"/> rather than an infinity.
    /// </remarks>
    /// <inheritdoc cref="GetValue(string, int, Context)"/>
    float GetValue(string configKey, float defaultValue, Context? context = null);

    /// <summary>Evaluates <paramref name="configKey"/> and reads its value as a decimal.</summary>
    /// <inheritdoc cref="GetValue(string, int, Context)"/>
    decimal GetValue(string configKey, decimal defaultValue, Context? context = null);

    /// <summary>Evaluates <paramref name="configKey"/> and reads its value as a boolean.</summary>
    /// <remarks>
    /// Only <c>"true"</c> and <c>"false"</c> read as a boolean, either casing. A number does not,
    /// so a config holding <c>1</c> yields <paramref name="defaultValue"/>.
    /// </remarks>
    /// <inheritdoc cref="GetValue(string, int, Context)"/>
    bool GetValue(string configKey, bool defaultValue, Context? context = null);

    /// <summary>Evaluates <paramref name="configKey"/> and reads its value as text.</summary>
    /// <remarks>
    /// The value is returned as the server spelled it, with no parsing, so any config reads as
    /// text: a boolean config gives <c>"true"</c>, and a JSON config gives its JSON.
    /// </remarks>
    /// <param name="configKey">The config to read.</param>
    /// <param name="defaultValue">Returned when the config is missing or unreachable.</param>
    /// <param name="context">Evaluated against targeting rules; may be null.</param>
    /// <returns>The evaluated value, or <paramref name="defaultValue"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="configKey"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="configKey"/> or <paramref name="defaultValue"/> is null.
    /// </exception>
    string GetValue(string configKey, string defaultValue, Context? context = null);

    /// <summary>Evaluates <paramref name="configKey"/> and reads its value as JSON.</summary>
    /// <remarks>
    /// The config's JSON comes back whole, whatever shape it is, so nothing is lost when the shape
    /// in the dashboard is not the one this application expects. Use <see cref="GetJsonValue{T}"/>
    /// to bind it to a type of your own instead.
    /// </remarks>
    /// <inheritdoc cref="GetValue(string, int, Context)"/>
    JsonElement GetValue(string configKey, JsonElement defaultValue, Context? context = null);

    /// <summary>
    /// Reads a JSON config and binds it to <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// System.Text.Json's rules apply, so any property <typeparamref name="T"/> does not declare is
    /// dropped: a config whose shape has moved on binds to <typeparamref name="T"/>'s own defaults
    /// and cannot be told apart from <paramref name="defaultValue"/>. Read the config with
    /// <see cref="GetValue(string, JsonElement, Context)"/> when that matters, or when the shape
    /// belongs to the dashboard rather than to this application.
    /// </remarks>
    /// <typeparam name="T">The type to bind the config's JSON to.</typeparam>
    /// <param name="configKey">The config to read.</param>
    /// <param name="defaultValue">Returned when the config is missing or is not valid JSON.</param>
    /// <param name="context">Evaluated against targeting rules; may be null.</param>
    /// <returns>The bound value, or <paramref name="defaultValue"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="configKey"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="configKey"/> or <paramref name="defaultValue"/> is null.
    /// </exception>
    [RequiresUnreferencedCode(Reflective.BindingNeedsReflection)]
    [RequiresDynamicCode(Reflective.BindingNeedsReflection)]
    T GetJsonValue<T>(string configKey, T defaultValue, Context? context = null)
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
    /// Calls <paramref name="onChange"/> with the newly evaluated integer whenever an update
    /// carries <paramref name="configKey"/>.
    /// </summary>
    /// <remarks>
    /// Handlers run on the thread the update arrived on, so one that blocks delays later updates.
    /// Register the watch before <see cref="InitializeAsync"/> to be called for the first config
    /// state as well. As with <c>GetValue</c>, there is one overload per type the SDK can read
    /// exactly, so a type it cannot fill is a compile error rather than a surprise at runtime.
    /// </remarks>
    /// <param name="configKey">The config to watch.</param>
    /// <param name="defaultValue">
    /// Passed to <paramref name="onChange"/> when the updated value will not read as this type.
    /// </param>
    /// <param name="onChange">Receives the newly evaluated value.</param>
    /// <param name="context">Evaluated against targeting rules; may be null.</param>
    /// <returns>A handle that cancels this watch. Disposing it twice is harmless.</returns>
    /// <exception cref="ArgumentException"><paramref name="configKey"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="configKey"/> or <paramref name="onChange"/> is null.
    /// </exception>
    IDisposable Watch(string configKey, int defaultValue, Action<int> onChange, Context? context = null);

    /// <summary>
    /// Calls <paramref name="onChange"/> with the newly evaluated long whenever an update
    /// carries <paramref name="configKey"/>.
    /// </summary>
    /// <inheritdoc cref="Watch(string, int, Action{int}, Context)"/>
    IDisposable Watch(string configKey, long defaultValue, Action<long> onChange, Context? context = null);

    /// <summary>
    /// Calls <paramref name="onChange"/> with the newly evaluated double whenever an update
    /// carries <paramref name="configKey"/>.
    /// </summary>
    /// <inheritdoc cref="Watch(string, int, Action{int}, Context)"/>
    IDisposable Watch(string configKey, double defaultValue, Action<double> onChange, Context? context = null);

    /// <summary>
    /// Calls <paramref name="onChange"/> with the newly evaluated float whenever an update
    /// carries <paramref name="configKey"/>.
    /// </summary>
    /// <inheritdoc cref="Watch(string, int, Action{int}, Context)"/>
    IDisposable Watch(string configKey, float defaultValue, Action<float> onChange, Context? context = null);

    /// <summary>
    /// Calls <paramref name="onChange"/> with the newly evaluated decimal whenever an update
    /// carries <paramref name="configKey"/>.
    /// </summary>
    /// <inheritdoc cref="Watch(string, int, Action{int}, Context)"/>
    IDisposable Watch(string configKey, decimal defaultValue, Action<decimal> onChange, Context? context = null);

    /// <summary>
    /// Calls <paramref name="onChange"/> with the newly evaluated boolean whenever an update
    /// carries <paramref name="configKey"/>.
    /// </summary>
    /// <inheritdoc cref="Watch(string, int, Action{int}, Context)"/>
    IDisposable Watch(string configKey, bool defaultValue, Action<bool> onChange, Context? context = null);

    /// <summary>
    /// Calls <paramref name="onChange"/> with the newly evaluated text whenever an update
    /// carries <paramref name="configKey"/>.
    /// </summary>
    /// <remarks>
    /// The value arrives as the server spelled it, with no parsing, so any config can be
    /// watched as text.
    /// </remarks>
    /// <inheritdoc cref="Watch(string, int, Action{int}, Context)"/>
    IDisposable Watch(string configKey, string defaultValue, Action<string> onChange, Context? context = null);

    /// <summary>
    /// Calls <paramref name="onChange"/> with the newly evaluated JSON whenever an update
    /// carries <paramref name="configKey"/>.
    /// </summary>
    /// <remarks>
    /// The config's JSON arrives whole, whatever shape it is. Use
    /// <see cref="WatchJson{T}"/> to bind it to a type of your own instead.
    /// </remarks>
    /// <inheritdoc cref="Watch(string, int, Action{int}, Context)"/>
    IDisposable Watch(string configKey, JsonElement defaultValue, Action<JsonElement> onChange, Context? context = null);

    /// <summary>
    /// Calls <paramref name="onChange"/> with the config's JSON bound to <typeparamref name="T"/>
    /// whenever an update carries <paramref name="configKey"/>.
    /// </summary>
    /// <remarks>
    /// The watching counterpart of <see cref="GetJsonValue{T}"/>, and it binds on the same terms:
    /// any property <typeparamref name="T"/> does not declare is dropped, so a config whose shape
    /// has moved on binds to <typeparamref name="T"/>'s own defaults and cannot be told apart from
    /// <paramref name="defaultValue"/>.
    /// </remarks>
    /// <typeparam name="T">The type to bind the config's JSON to.</typeparam>
    /// <param name="configKey">The config to watch.</param>
    /// <param name="defaultValue">
    /// Passed to <paramref name="onChange"/> when the config is missing or is not valid JSON.
    /// </param>
    /// <param name="onChange">Receives the newly bound value.</param>
    /// <param name="context">Evaluated against targeting rules; may be null.</param>
    /// <returns>A handle that cancels this watch. Disposing it twice is harmless.</returns>
    /// <exception cref="ArgumentException"><paramref name="configKey"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="context"/> is null.</exception>
    [RequiresUnreferencedCode(Reflective.BindingNeedsReflection)]
    [RequiresDynamicCode(Reflective.BindingNeedsReflection)]
    IDisposable WatchJson<T>(string configKey, T defaultValue, Action<T> onChange, Context? context = null)
        where T : notnull;

    /// <summary>Cancels every watch on one config.</summary>
    /// <param name="configKey">The config to stop watching.</param>
    void Unwatch(string configKey);

    /// <summary>Cancels every watch on every config.</summary>
    void UnwatchAll();
}
