using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using ConfigDirector.Tests.Integration;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Error;
using OpenFeature.Model;

namespace ConfigDirector.OpenFeature.Tests;

public sealed class OpenFeatureApiTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = SdkServer.PatientTimeout;
    private static readonly TimeSpan WindowForAnEarlyReady = TimeSpan.FromMilliseconds(300);

    private readonly SdkServer _server = new();
    private readonly List<(ProviderEventTypes Type, EventHandlerDelegate Handler)> _handlers = [];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ResolvesThroughAnOpenFeatureClient()
    {
        var client = await ClientServingAsync(Bundles.Of(
            Bundles.Config("show-banner", "boolean", "true"),
            Bundles.Config("greeting", "string", "\"Bye\"")));

        (await client.GetBooleanValueAsync("show-banner", false, cancellationToken: Cancellation)).ShouldBeTrue();
        var details = await client.GetStringDetailsAsync("greeting", "Hello", cancellationToken: Cancellation);
        details.Value.ShouldBe("Bye");
        details.Reason.ShouldBe(Reason.TargetingMatch);
        details.Variant.ShouldBe("dv-greeting");
        details.ErrorType.ShouldBe(ErrorType.None);
    }

    [Fact]
    public async Task RunsAnAfterHookThatSeesTargetingMatchAndTheValueIdForAServedValue()
    {
        var client = await ClientServingAsync(Bundles.Of(Bundles.Config("show-banner", "boolean", "true")));
        var hook = new RecordingHook();
        client.AddHooks([hook]);

        await client.GetBooleanValueAsync("show-banner", false, cancellationToken: Cancellation);

        var served = hook.Served.ShouldHaveSingleItem();
        served.Reason.ShouldBe(Reason.TargetingMatch);
        served.Variant.ShouldBe("dv-show-banner");
    }

    [Fact]
    public async Task RunsAnErrorHookThatSeesFlagNotFoundForAConfigThatDoesNotExist()
    {
        var client = await ClientServingAsync(Bundles.Of());
        var hook = new RecordingHook();
        client.AddHooks([hook]);

        await client.GetBooleanValueAsync("missing-flag", false, cancellationToken: Cancellation);

        var error = hook.Errors.ShouldHaveSingleItem().ShouldBeOfType<FeatureProviderException>();
        error.ErrorType.ShouldBe(ErrorType.FlagNotFound);
    }

    [Fact]
    public async Task CarriesTheClientsEvaluationContextToTheProvider()
    {
        var client = await ClientServingAsync(Bundles.Of(
            Bundles.Config("by-id", "string", "\"default\"", Bundles.Rule("identifier", null, "user-1", "matched"))));
        var context = EvaluationContext.Builder().SetTargetingKey("user-1").Build();

        (await client.GetStringValueAsync("by-id", "x", context, cancellationToken: Cancellation)).ShouldBe("matched");
        (await client.GetStringValueAsync("by-id", "x", cancellationToken: Cancellation)).ShouldBe("default");
    }

    [Fact]
    public async Task RaisesConfigurationChangedHandlersWhenAnUpdateArrives()
    {
        var changed = new TaskCompletionSource<ProviderEventPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(ProviderEventPayload? payload) => changed.TrySetResult(payload!);
        Api.Instance.AddHandler(ProviderEventTypes.ProviderConfigurationChanged, Handler);
        try
        {
            var client = await ClientServingAsync(Bundles.Of(Bundles.Config("greeting", "string", "\"Bye\"")));

            _server.Push(Bundles.DeltaOf(Bundles.Config("greeting", "string", "\"Ciao\"")));

            var payload = await changed.Task.WaitAsync(Timeout, Cancellation);
            payload.FlagsChanged.ShouldBe(["greeting"]);
            for (var attempt = 0; attempt < 100 && await client.GetStringValueAsync("greeting", "Hello", cancellationToken: Cancellation) != "Ciao"; attempt++)
            {
                await Task.Delay(10, Cancellation);
            }

            (await client.GetStringValueAsync("greeting", "Hello", cancellationToken: Cancellation)).ShouldBe("Ciao");
        }
        finally
        {
            Api.Instance.RemoveHandler(ProviderEventTypes.ProviderConfigurationChanged, Handler);
        }
    }

    [Fact]
    public async Task ReportsTheProviderMetadataToTheApi()
    {
        await ClientServingAsync(Bundles.Of());

        Api.Instance.GetProviderMetadata()!.Name.ShouldBe("ConfigDirectorProvider");
    }

    [Fact]
    public async Task ShuttingTheApiDownClosesTheProvider()
    {
        _server.Bundle = Bundles.Of(Bundles.Config("show-banner", "boolean", "true"));
        var provider = Provider();
        await Api.Instance.SetProviderAsync(provider, Cancellation);
        (await provider.ResolveBooleanValueAsync("show-banner", false, null, Cancellation)).Value.ShouldBeTrue();

        await Api.Instance.ShutdownAsync();

        (await provider.ResolveBooleanValueAsync("show-banner", false, null, Cancellation)).Value.ShouldBeFalse();
    }

    [Fact]
    public async Task PutsTheProviderInTheErrorStateWhenItsOfflineValuesCannotBeLoaded()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"configdirector-missing-{Guid.NewGuid():N}.json");
        var failed = new TaskCompletionSource<ProviderEventPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(ProviderEventPayload? payload) => failed.TrySetResult(payload!);
        Api.Instance.AddHandler(ProviderEventTypes.ProviderError, Handler);
        try
        {
            await Api.Instance.SetProviderAsync(
                new ConfigDirectorProvider(new ConfigDirectorClientOptions
                {
                    Connection = { Mode = ConnectionMode.Offline, File = missing },
                }),
                Cancellation);

            var client = Api.Instance.GetClient();
            var details = await client.GetBooleanDetailsAsync("new-checkout", true, cancellationToken: Cancellation);

            client.ProviderStatus.ShouldBe(ProviderStatus.Error);
            (await failed.Task.WaitAsync(Timeout, Cancellation)).Message!.ShouldContain(missing);
            details.Value.ShouldBeTrue();
            details.ErrorType.ShouldBe(ErrorType.ProviderNotReady);
        }
        finally
        {
            Api.Instance.RemoveHandler(ProviderEventTypes.ProviderError, Handler);
        }
    }

    [Fact]
    public async Task FailsToInitializeWhenNoConfigArrivesWithinTheTimeout()
    {
        var errors = RecordedEvents(ProviderEventTypes.ProviderError);
        var changes = RecordedEvents(ProviderEventTypes.ProviderConfigurationChanged);
        _server.Bundle = Bundles.Of(Bundles.Config("show-banner", "boolean", "true"));
        _server.Replies(HttpStatusCode.BadGateway, "try again");

        await Api.Instance.SetProviderAsync(Provider(TimeSpan.FromMilliseconds(200)), Cancellation);
        var client = Api.Instance.GetClient();
        var status = client.ProviderStatus;
        var details = await client.GetBooleanDetailsAsync("show-banner", false, cancellationToken: Cancellation);

        await WaitUntilAsync(() => !changes.IsEmpty);

        status.ShouldBe(ProviderStatus.Error);
        details.Value.ShouldBeFalse();
        details.ErrorType.ShouldBe(ErrorType.ProviderNotReady);
        errors.Count.ShouldBe(1);
    }

    [Fact]
    public async Task FailsToInitializeWhenTheSdkKeyIsRejected()
    {
        var errors = RecordedEvents(ProviderEventTypes.ProviderError);
        _server.Bundle = Bundles.Of(Bundles.Config("show-banner", "boolean", "true"));
        _server.Replies(HttpStatusCode.Unauthorized);

        await Api.Instance.SetProviderAsync(Provider(), Cancellation);
        var client = Api.Instance.GetClient();
        var details = await client.GetBooleanDetailsAsync("show-banner", false, cancellationToken: Cancellation);

        await WaitUntilAsync(() => !errors.IsEmpty);

        client.ProviderStatus.ShouldBe(ProviderStatus.Error);
        details.Value.ShouldBeFalse();
        details.ErrorType.ShouldBe(ErrorType.ProviderNotReady);
        errors.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ReportsReadyOnceWhenConfigArrivesAfterAFailedInitialization()
    {
        var ready = RecordedEvents(ProviderEventTypes.ProviderReady);
        var changes = RecordedEvents(ProviderEventTypes.ProviderConfigurationChanged);
        _server.Bundle = Bundles.Of(Bundles.Config("show-banner", "boolean", "true"));
        _server.Replies(HttpStatusCode.BadGateway, "try again");
        await Api.Instance.SetProviderAsync(Provider(TimeSpan.FromMilliseconds(200)), Cancellation);
        var client = Api.Instance.GetClient();
        var statusAfterTheFailure = client.ProviderStatus;

        await WaitUntilAsync(() => !ready.IsEmpty);
        _server.Push(Bundles.DeltaOf(Bundles.Config("greeting", "string", "\"Ciao\"")));
        await WaitUntilAsync(() => changes.Count == 2);

        statusAfterTheFailure.ShouldBe(ProviderStatus.Error);
        ready.Count.ShouldBe(1);
        client.ProviderStatus.ShouldBe(ProviderStatus.Ready);
        (await client.GetBooleanValueAsync("show-banner", false, cancellationToken: Cancellation)).ShouldBeTrue();
    }

    [Fact]
    public async Task ReportsReadyWhenConfigArrivesWhileOpenFeatureRecordsTheFailure()
    {
        var changes = RecordedEvents(ProviderEventTypes.ProviderConfigurationChanged);
        var ready = RecordedEvents(ProviderEventTypes.ProviderReady);
        var configArrivedBeforeTheFailure = false;
        _server.Bundle = Bundles.Of(Bundles.Config("show-banner", "boolean", "true"));
        _server.Replies(HttpStatusCode.BadGateway, "try again");
        var provider = Provider(TimeSpan.FromMilliseconds(200), readinessCheck =>
        {
            configArrivedBeforeTheFailure = SpinWait.SpinUntil(() => !changes.IsEmpty, Timeout);
            _ = Task.Run(readinessCheck);
            SpinWait.SpinUntil(() => !ready.IsEmpty, WindowForAnEarlyReady);
        });

        await Api.Instance.SetProviderAsync(provider, Cancellation);

        var client = Api.Instance.GetClient();
        await WaitUntilAsync(() => !ready.IsEmpty && client.ProviderStatus == ProviderStatus.Ready);
        _server.Push(Bundles.DeltaOf(Bundles.Config("greeting", "string", "\"Ciao\"")));
        await WaitUntilAsync(() => changes.Count == 2);

        configArrivedBeforeTheFailure.ShouldBeTrue();
        client.ProviderStatus.ShouldBe(ProviderStatus.Ready);
        ready.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ReportsReadyOnceWhenConfigArrivesWithinTheTimeout()
    {
        var ready = RecordedEvents(ProviderEventTypes.ProviderReady);
        var changes = RecordedEvents(ProviderEventTypes.ProviderConfigurationChanged);
        await ClientServingAsync(Bundles.Of(Bundles.Config("show-banner", "boolean", "true")));

        _server.Push(Bundles.DeltaOf(Bundles.Config("greeting", "string", "\"Ciao\"")));
        await WaitUntilAsync(() => changes.Count == 2);

        ready.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ReportsReadyWhenConfigArrivesAfterACancelledInitialization()
    {
        var ready = RecordedEvents(ProviderEventTypes.ProviderReady);
        _server.Bundle = Bundles.Of(Bundles.Config("show-banner", "boolean", "true"));
        _server.Replies(HttpStatusCode.BadGateway, "try again");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(Api.Instance.SetProviderAsync(Provider(), cancellation.Token));

        await WaitUntilAsync(() => !ready.IsEmpty);

        var client = Api.Instance.GetClient();
        client.ProviderStatus.ShouldBe(ProviderStatus.Ready);
        (await client.GetBooleanValueAsync("show-banner", false, cancellationToken: Cancellation)).ShouldBeTrue();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (type, handler) in _handlers)
        {
            Api.Instance.RemoveHandler(type, handler);
        }

        await Api.Instance.ShutdownAsync();
        _server.Dispose();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var waited = Stopwatch.StartNew();
        while (!condition() && waited.Elapsed < Timeout)
        {
            await Task.Delay(10, Cancellation);
        }
    }

    private ConcurrentQueue<ProviderEventPayload> RecordedEvents(ProviderEventTypes type)
    {
        var events = new ConcurrentQueue<ProviderEventPayload>();
        EventHandlerDelegate handler = payload => events.Enqueue(payload!);
        Api.Instance.AddHandler(type, handler);
        _handlers.Add((type, handler));
        return events;
    }

    private ConfigDirectorProvider Provider(TimeSpan? timeout = null) => new("sdk-key", ServerOptions(timeout));

    private ConfigDirectorProvider Provider(TimeSpan timeout, Action<Func<Task>> startReadinessCheck) =>
        new("sdk-key", ServerOptions(timeout), startReadinessCheck);

    private ConfigDirectorClientOptions ServerOptions(TimeSpan? timeout)
    {
        var options = new ConfigDirectorClientOptions();
        if (timeout is { } chosen)
        {
            options.Connection.Timeout = chosen;
        }

        _server.Attach(options);
        return options;
    }

    private async Task<IFeatureClient> ClientServingAsync(string bundle)
    {
        _server.Bundle = bundle;
        await Api.Instance.SetProviderAsync(Provider(), Cancellation);
        return Api.Instance.GetClient();
    }

    private sealed class RecordingHook : Hook
    {
        internal ConcurrentQueue<(string? Reason, string? Variant)> Served { get; } = new();

        internal ConcurrentQueue<Exception> Errors { get; } = new();

        public override ValueTask AfterAsync<T>(
            HookContext<T> context,
            FlagEvaluationDetails<T> details,
            IReadOnlyDictionary<string, object>? hints = null,
            CancellationToken cancellationToken = default)
        {
            Served.Enqueue((details.Reason, details.Variant));
            return default;
        }

        public override ValueTask ErrorAsync<T>(
            HookContext<T> context,
            Exception error,
            IReadOnlyDictionary<string, object>? hints = null,
            CancellationToken cancellationToken = default)
        {
            Errors.Enqueue(error);
            return default;
        }
    }
}
