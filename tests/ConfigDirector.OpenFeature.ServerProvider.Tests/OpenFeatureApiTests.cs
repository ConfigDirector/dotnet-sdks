using ConfigDirector.Tests.Integration;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace ConfigDirector.OpenFeature.Tests;

public sealed class OpenFeatureApiTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = SdkServer.PatientTimeout;

    private readonly SdkServer _server = new();

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

    public async ValueTask DisposeAsync()
    {
        await Api.Instance.ShutdownAsync();
        _server.Dispose();
    }

    private ConfigDirectorProvider Provider()
    {
        var options = new ConfigDirectorClientOptions();
        _server.Attach(options);
        return new ConfigDirectorProvider("sdk-key", options);
    }

    private async Task<IFeatureClient> ClientServingAsync(string bundle)
    {
        _server.Bundle = bundle;
        await Api.Instance.SetProviderAsync(Provider(), Cancellation);
        return Api.Instance.GetClient();
    }
}
