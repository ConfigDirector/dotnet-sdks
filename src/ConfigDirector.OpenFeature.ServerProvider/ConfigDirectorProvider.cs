using System.Text.Json;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Error;
using OpenFeature.Model;
using OpenFeatureMetadata = OpenFeature.Model.Metadata;
using OpenFeatureValue = OpenFeature.Model.Value;

namespace ConfigDirector.OpenFeature;

/// <summary>
/// An OpenFeature provider backed by the ConfigDirector .NET server SDK.
/// </summary>
/// <remarks>
/// <para>
/// Register it with the OpenFeature API and read values through an OpenFeature client. The provider
/// owns a <see cref="ConfigDirectorClient"/>: it connects when OpenFeature initializes the provider,
/// and closes when OpenFeature shuts it down or the provider is disposed. Targeting rules are
/// evaluated locally, so an evaluation makes no network calls.
/// </para>
/// <code>
/// await Api.Instance.SetProviderAsync(new ConfigDirectorProvider(serverSdkKey));
/// var client = Api.Instance.GetClient();
///
/// var enabled = await client.GetBooleanValueAsync("new-checkout", false);
/// </code>
/// <para>
/// The OpenFeature evaluation context maps onto the ConfigDirector <see cref="Context"/>: the
/// targeting key, or failing that an <c>id</c> attribute, becomes the context's id, a top-level
/// <c>kind</c> its kind (<c>user</c> when omitted), <c>name</c> its name, a <c>traits</c> structure
/// its traits, and a boolean <c>anonymous</c> its anonymous flag.
/// </para>
/// <para>
/// If no config arrives within the connection timeout, or ConfigDirector rejects the server SDK
/// key, initialization fails with OpenFeature's <see cref="ProviderNotReadyException"/>.
/// <see cref="Api.SetProviderAsync(FeatureProvider, CancellationToken)"/> still completes, and
/// OpenFeature reports the <see cref="ProviderStatus.Error"/> status with a
/// <see cref="ProviderEventTypes.ProviderError"/> event. Evaluations return their in-code default
/// values with the error <see cref="ErrorType.ProviderNotReady"/> until config arrives. After a
/// timeout, or an initialization the caller cancelled, the provider keeps connecting, and emits
/// <see cref="ProviderEventTypes.ProviderReady"/> once config arrives.
/// </para>
/// <para>
/// In <see cref="ConnectionMode.Offline"/> the provider never connects and serves the values it was
/// given, and needs no server SDK key. If those values cannot be loaded, initialization fails:
/// OpenFeature puts the provider in its error state and emits
/// <see cref="ProviderEventTypes.ProviderError"/>, and evaluations return their in-code default
/// values with the error <see cref="ErrorType.ProviderNotReady"/>.
/// </para>
/// </remarks>
public sealed class ConfigDirectorProvider : FeatureProvider, IDisposable, IAsyncDisposable
{
    private const string Name = "ConfigDirectorProvider";

    private const string NoConfigInTime =
        "No config arrived from ConfigDirector within the connection timeout. Flags resolve to their "
        + "in-code default values until config arrives.";

    private static readonly OpenFeatureMetadata ProviderMetadata = new(Name);

    private static readonly SdkIdentity Identity =
        SdkIdentity.For("dotnet-openfeature-server-provider", typeof(ConfigDirectorProvider).Assembly);

    private static readonly TimeSpan TimeAllowedForOpenFeatureToRecordTheFailure = TimeSpan.FromSeconds(1);

    [ThreadStatic]
    private static ConfigEvaluation? _evaluated;

    private readonly ConfigDirectorClient _client;
    private readonly Action<Func<Task>> _startReadinessCheck;
    private readonly object _lock = new();
    private Startup _startup = Startup.Initializing;

    /// <summary>
    /// Builds a provider whose client uses the default settings.
    /// </summary>
    /// <param name="serverSdkKey">A secret; do not commit it to source control.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverSdkKey"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverSdkKey"/> is empty or whitespace.</exception>
    public ConfigDirectorProvider(string? serverSdkKey)
        : this(serverSdkKey, null)
    {
    }

    /// <summary>
    /// Builds a provider without a server SDK key, which only <see cref="ConnectionMode.Offline"/>
    /// allows. <paramref name="options"/> are the same settings a <see cref="ConfigDirectorClient"/>
    /// accepts.
    /// </summary>
    /// <param name="options">The settings to build the client with.</param>
    /// <exception cref="ArgumentNullException">
    /// The connection mode in <paramref name="options"/> is not <see cref="ConnectionMode.Offline"/>,
    /// so a server SDK key is required.
    /// </exception>
    public ConfigDirectorProvider(ConfigDirectorClientOptions options)
        : this(null, options)
    {
    }

    /// <summary>
    /// Builds a provider whose client uses <paramref name="options"/>. These are the same settings
    /// a <see cref="ConfigDirectorClient"/> accepts.
    /// </summary>
    /// <param name="serverSdkKey">
    /// A secret; do not commit it to source control. May be null or empty only in
    /// <see cref="ConnectionMode.Offline"/>, which ignores it.
    /// </param>
    /// <param name="options">The settings to build the client with, or null for the defaults.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="serverSdkKey"/> is null and the connection mode is not <see cref="ConnectionMode.Offline"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="serverSdkKey"/> is empty or whitespace and the connection mode is not
    /// <see cref="ConnectionMode.Offline"/>.
    /// </exception>
    public ConfigDirectorProvider(string? serverSdkKey, ConfigDirectorClientOptions? options)
        : this(serverSdkKey, options, StartOnTheThreadPool)
    {
    }

    internal ConfigDirectorProvider(
        string? serverSdkKey, ConfigDirectorClientOptions? options, Action<Func<Task>> startReadinessCheck)
    {
        _startReadinessCheck = startReadinessCheck;
        _client = new ConfigDirectorClient(serverSdkKey, options, Identity);
        _client.ConfigEvaluated += (_, args) => _evaluated = args.Evaluation;
        _client.ConfigsUpdated += (_, args) => Emit(new ProviderEventPayload
        {
            Type = ProviderEventTypes.ProviderConfigurationChanged,
            ProviderName = Name,
            FlagsChanged = [.. args.Keys, .. args.RemovedKeys],
        });
        _client.ClientReady += (_, _) => ReportReadyIfConfigArrived();
    }

    /// <inheritdoc/>
    public override OpenFeatureMetadata GetMetadata() => ProviderMetadata;

    /// <inheritdoc/>
    /// <exception cref="ProviderNotReadyException">
    /// No config arrived within the connection timeout, or ConfigDirector rejected the server SDK key.
    /// </exception>
    public override async Task InitializeAsync(EvaluationContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ReportReadyOnceTheFailureIsRecorded();
            throw;
        }

        lock (_lock)
        {
            if (_client.IsReady)
            {
                _startup = Startup.Ready;
                return;
            }
        }

        ReportReadyOnceTheFailureIsRecorded();
        throw new ProviderNotReadyException(NoConfigInTime);
    }

    /// <summary>
    /// Closes the connection. The same as OpenFeature shutting the provider down; disposing twice
    /// is harmless.
    /// </summary>
    public override Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        DisposeAsync().AsTask();

    /// <summary>
    /// Closes the connection. Disposing twice is harmless.
    /// </summary>
    public void Dispose() => _client.Dispose();

    /// <summary>
    /// Closes the connection. Disposing twice is harmless.
    /// </summary>
    /// <returns>A task that completes once the connection has been released.</returns>
    public ValueTask DisposeAsync() => _client.DisposeAsync();

    /// <inheritdoc/>
    public override Task<ResolutionDetails<bool>> ResolveBooleanValueAsync(
        string flagKey, bool defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context, static (client, key, fallback, mapped) => client.GetValue(key, fallback, mapped)));

    /// <inheritdoc/>
    public override Task<ResolutionDetails<string>> ResolveStringValueAsync(
        string flagKey, string defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context, static (client, key, fallback, mapped) => client.GetValue(key, fallback, mapped)));

    /// <inheritdoc/>
    public override Task<ResolutionDetails<int>> ResolveIntegerValueAsync(
        string flagKey, int defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context, static (client, key, fallback, mapped) => client.GetValue(key, fallback, mapped)));

    /// <inheritdoc/>
    public override Task<ResolutionDetails<double>> ResolveDoubleValueAsync(
        string flagKey, double defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context, static (client, key, fallback, mapped) => client.GetValue(key, fallback, mapped)));

    /// <inheritdoc/>
    public override Task<ResolutionDetails<OpenFeatureValue>> ResolveStructureValueAsync(
        string flagKey, OpenFeatureValue defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default)
    {
        if (defaultValue is null)
        {
            throw new ArgumentNullException(nameof(defaultValue));
        }

        var fallback = ValueMapper.ToJsonElement(defaultValue);
        var (element, evaluation) = Evaluate(flagKey, fallback, context, static (client, key, fallback, mapped) => client.GetValue(key, fallback, mapped));
        if (evaluation is null || evaluation.IsDefault)
        {
            return Task.FromResult(Resolution(flagKey, defaultValue, evaluation));
        }

        return Task.FromResult(MissingShape(element, defaultValue) switch
        {
            null => Resolution(flagKey, ValueMapper.ToValue(element), evaluation),
            var requiredShape => Resolutions.TypeMismatch(flagKey, defaultValue, requiredShape),
        });
    }

    private static string? MissingShape(JsonElement servedValue, OpenFeatureValue defaultValue) =>
        (defaultValue, servedValue.ValueKind) switch
        {
            ({ IsStructure: true }, not JsonValueKind.Object) => "a JSON object",
            ({ IsList: true }, not JsonValueKind.Array) => "a JSON array",
            _ => null,
        };

    private ResolutionDetails<T> Resolve<T>(string flagKey, T defaultValue, EvaluationContext? context, Reader<T> read)
    {
        var (value, evaluation) = Evaluate(flagKey, defaultValue, context, read);
        return Resolution(flagKey, value, evaluation);
    }

    private ResolutionDetails<T> Resolution<T>(string flagKey, T value, ConfigEvaluation? evaluation) =>
        evaluation is null && _client.IsClosed
            ? Resolutions.Closed(flagKey, value)
            : Resolutions.Of(flagKey, value, evaluation);

    private (T Value, ConfigEvaluation? Evaluation) Evaluate<T>(
        string flagKey, T defaultValue, EvaluationContext? context, Reader<T> read)
    {
        var mapped = ContextMapper.ToContext(context);
        _evaluated = null;
        try
        {
            var value = read(_client, flagKey, defaultValue, mapped);
            return (value, _evaluated);
        }
        finally
        {
            _evaluated = null;
        }
    }

    private static void StartOnTheThreadPool(Func<Task> readinessCheck) => _ = Task.Run(readinessCheck);

    private void ReportReadyOnceTheFailureIsRecorded()
    {
        lock (_lock)
        {
            _startup = Startup.AwaitingRecordedFailure;
        }

        _startReadinessCheck(async () =>
        {
            await Task.Delay(TimeAllowedForOpenFeatureToRecordTheFailure).ConfigureAwait(false);
            lock (_lock)
            {
                _startup = Startup.AwaitingConfig;
            }

            ReportReadyIfConfigArrived();
        });
    }

    private void ReportReadyIfConfigArrived()
    {
        lock (_lock)
        {
            var configArrivedAfterTheFailure = _startup == Startup.AwaitingConfig && _client.IsReady;
            if (!configArrivedAfterTheFailure)
            {
                return;
            }

            _startup = Startup.Ready;
        }

        Emit(new ProviderEventPayload
        {
            Type = ProviderEventTypes.ProviderReady,
            ProviderName = Name,
        });
    }

    private void Emit(ProviderEventPayload payload)
    {
        if (!EventChannel.Writer.TryWrite(payload))
        {
            _ = EventChannel.Writer.WriteAsync(payload).AsTask();
        }
    }

    private delegate T Reader<T>(IConfigDirectorClient client, string key, T defaultValue, Context? context);

    private enum Startup
    {
        Initializing,
        AwaitingRecordedFailure,
        AwaitingConfig,
        Ready,
    }
}
