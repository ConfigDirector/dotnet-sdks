using OpenFeature;
using OpenFeature.Constant;
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
/// targeting key, or failing that an <c>id</c> attribute, becomes the context's id, <c>name</c> its
/// name, a <c>traits</c> structure its traits, and a boolean <c>anonymous</c> its anonymous flag.
/// </para>
/// <para>
/// If the first config state does not arrive within the configured timeout, initialization still
/// completes and evaluations return their defaults with the error <see cref="ErrorType.ProviderNotReady"/>.
/// The provider keeps connecting, and emits <see cref="ProviderEventTypes.ProviderReady"/> once
/// config state arrives.
/// </para>
/// </remarks>
public sealed class ConfigDirectorProvider : FeatureProvider, IDisposable, IAsyncDisposable
{
    private const string Name = "ConfigDirectorProvider";

    private static readonly OpenFeatureMetadata ProviderMetadata = new(Name);

    private static readonly SdkIdentity Identity =
        SdkIdentity.For("dotnet-openfeature-server-provider", typeof(ConfigDirectorProvider).Assembly);

    [ThreadStatic]
    private static ConfigEvaluation? _evaluated;

    private readonly ConfigDirectorClient _client;

    /// <summary>
    /// Builds a provider whose client uses the default settings.
    /// </summary>
    /// <param name="serverSdkKey">A secret; do not commit it to source control.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverSdkKey"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverSdkKey"/> is empty or whitespace.</exception>
    public ConfigDirectorProvider(string serverSdkKey)
        : this(serverSdkKey, null)
    {
    }

    /// <summary>
    /// Builds a provider whose client uses <paramref name="options"/>. These are the same settings
    /// a <see cref="ConfigDirectorClient"/> accepts.
    /// </summary>
    /// <param name="serverSdkKey">A secret; do not commit it to source control.</param>
    /// <param name="options">The settings to build the client with, or null for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverSdkKey"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverSdkKey"/> is empty or whitespace.</exception>
    public ConfigDirectorProvider(string serverSdkKey, ConfigDirectorClientOptions? options)
    {
        _client = new ConfigDirectorClient(serverSdkKey, options, Identity);
        _client.ConfigEvaluated += (_, args) => _evaluated = args.Evaluation;
        _client.ConfigsUpdated += (_, args) => Emit(new ProviderEventPayload
        {
            Type = ProviderEventTypes.ProviderConfigurationChanged,
            ProviderName = Name,
            FlagsChanged = [.. args.Keys],
        });
    }

    /// <inheritdoc/>
    public override OpenFeatureMetadata GetMetadata() => ProviderMetadata;

    /// <inheritdoc/>
    public override async Task InitializeAsync(EvaluationContext context, CancellationToken cancellationToken = default)
    {
        await _client.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!_client.IsReady)
        {
            _client.ClientReady += (_, _) => Emit(new ProviderEventPayload
            {
                Type = ProviderEventTypes.ProviderReady,
                ProviderName = Name,
            });
        }
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
        var usedDefault = evaluation is null || evaluation.IsDefault;

        return Task.FromResult(Resolution(flagKey, usedDefault ? defaultValue : ValueMapper.ToValue(element), evaluation));
    }

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

    private void Emit(ProviderEventPayload payload)
    {
        if (!EventChannel.Writer.TryWrite(payload))
        {
            _ = EventChannel.Writer.WriteAsync(payload).AsTask();
        }
    }

    private delegate T Reader<T>(IConfigDirectorClient client, string key, T defaultValue, Context? context);
}
