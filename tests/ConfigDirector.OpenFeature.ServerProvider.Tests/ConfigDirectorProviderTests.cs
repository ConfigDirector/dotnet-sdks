using System.Net;
using System.Text.Json;
using ConfigDirector.Tests.Integration;
using OpenFeature.Constant;
using OpenFeature.Model;
using OpenFeatureValue = OpenFeature.Model.Value;

namespace ConfigDirector.OpenFeature.Tests;

public sealed class ConfigDirectorProviderTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly SdkServer _server = new();
    private readonly List<ConfigDirectorProvider> _providers = [];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ResolvesEachFlagTypeOnceTheProviderIsReady()
    {
        var provider = await ReadyAsync(Bundles.Of(
            Bundles.Config("show-banner", "boolean", "true"),
            Bundles.Config("greeting", "string", "\"Bye\""),
            Bundles.Config("max-items", "integer", "42"),
            Bundles.Config("rate", "float", "1.5"),
            Bundles.Config("settings", "json", "{\"theme\":\"dark\",\"sizes\":[1,2.5],\"extra\":null}"),
            Bundles.Config("tags", "json", "[\"a\",\"b\"]")));

        (await provider.ResolveBooleanValueAsync("show-banner", false, null, Cancellation)).Value.ShouldBeTrue();
        (await provider.ResolveStringValueAsync("greeting", "Hello", null, Cancellation)).Value.ShouldBe("Bye");
        (await provider.ResolveIntegerValueAsync("max-items", 0, null, Cancellation)).Value.ShouldBe(42);
        (await provider.ResolveDoubleValueAsync("rate", 0.0, null, Cancellation)).Value.ShouldBe(1.5);

        var settings = (await provider.ResolveStructureValueAsync("settings", new OpenFeatureValue(Structure.Empty), null, Cancellation)).Value;
        settings.AsStructure!.GetValue("theme").AsString.ShouldBe("dark");
        settings.AsStructure.GetValue("sizes").AsList.ShouldBe([new OpenFeatureValue(1), new OpenFeatureValue(2.5)]);
        settings.AsStructure.GetValue("extra").IsNull.ShouldBeTrue();

        var tags = (await provider.ResolveStructureValueAsync("tags", new OpenFeatureValue(new List<OpenFeatureValue>()), null, Cancellation)).Value;
        tags.AsList.ShouldBe([new OpenFeatureValue("a"), new OpenFeatureValue("b")]);

        _server.Bodies[0].ShouldContain("\"serverSdkKey\":\"sdk-key\"");
    }

    [Fact]
    public async Task ReadsAWholeNumberAsADouble()
    {
        var provider = await ReadyAsync(Bundles.Of(Bundles.Config("max-items", "integer", "42")));

        (await provider.ResolveDoubleValueAsync("max-items", 0.0, null, Cancellation)).Value.ShouldBe(42.0);
    }

    [Fact]
    public async Task ReportsAMatchWithTheValueIdAsTheVariant()
    {
        var provider = await ReadyAsync(Bundles.Of(Bundles.Config("greeting", "string", "\"Bye\"")));

        var details = await provider.ResolveStringValueAsync("greeting", "Hello", null, Cancellation);

        details.Value.ShouldBe("Bye");
        details.FlagKey.ShouldBe("greeting");
        details.Reason.ShouldBe(Reason.TargetingMatch);
        details.Variant.ShouldBe("dv-greeting");
        details.ErrorType.ShouldBe(ErrorType.None);
        details.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task ReturnsTheDefaultWithFlagNotFoundForAConfigTheServerDidNotSend()
    {
        var provider = await ReadyAsync(Bundles.Of());

        var details = await provider.ResolveBooleanValueAsync("missing-flag", true, null, Cancellation);

        details.Value.ShouldBeTrue();
        details.Reason.ShouldBe(Reason.Error);
        details.ErrorType.ShouldBe(ErrorType.FlagNotFound);
        details.ErrorMessage!.ShouldContain("missing-flag");
        (await provider.ResolveStringValueAsync("missing-flag", "fallback", null, Cancellation)).Value.ShouldBe("fallback");
        (await provider.ResolveIntegerValueAsync("missing-flag", 7, null, Cancellation)).Value.ShouldBe(7);
    }

    [Fact]
    public async Task ReturnsTheDefaultWithTypeMismatchForAValueOfAnotherType()
    {
        var provider = await ReadyAsync(Bundles.Of(Bundles.Config("greeting", "string", "\"Bye\"")));

        var number = await provider.ResolveIntegerValueAsync("greeting", 7, null, Cancellation);
        var structure = await provider.ResolveStructureValueAsync("greeting", new OpenFeatureValue(Structure.Empty), null, Cancellation);

        number.Value.ShouldBe(7);
        number.Reason.ShouldBe(Reason.Error);
        number.ErrorType.ShouldBe(ErrorType.TypeMismatch);
        number.ErrorMessage!.ShouldContain("greeting");
        number.ErrorMessage!.ShouldContain("invalid-number");
        structure.ErrorType.ShouldBe(ErrorType.TypeMismatch);
        structure.ErrorMessage!.ShouldContain("invalid-json");
    }

    [Fact]
    public async Task ReturnsTheSameStructureDefaultItWasGiven()
    {
        var provider = await ReadyAsync(Bundles.Of());
        var defaultValue = new OpenFeatureValue(Structure.Builder().Set("since", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Build());

        var details = await provider.ResolveStructureValueAsync("missing-flag", defaultValue, null, Cancellation);

        details.Value.ShouldBeSameAs(defaultValue);
    }

    [Fact]
    public async Task ReportsTheStructureDefaultItWasGivenToTelemetryAsAJsonDocument()
    {
        var provider = await ReadyAsync(Bundles.Of());
        var threeRetries = new OpenFeatureValue(Structure.Builder().Set("retries", 3).Build());
        var fourRetries = new OpenFeatureValue(Structure.Builder().Set("retries", 4).Build());

        await provider.ResolveStructureValueAsync("missing-flag", threeRetries, null, Cancellation);
        await provider.ResolveStructureValueAsync("missing-flag", threeRetries, null, Cancellation);
        await provider.ResolveStructureValueAsync("other-flag", fourRetries, null, Cancellation);
        await provider.ShutdownAsync(Cancellation);

        var events = JsonDocument.Parse(await TelemetryBodyAsync()).RootElement
            .GetProperty("aggregatedEvents").GetProperty("evaluatedConfig").EnumerateArray()
            .Select(aggregated => aggregated.GetProperty("event"))
            .ToDictionary(evaluated => evaluated.GetProperty("key").GetString()!);
        var missing = events["missing-flag"];
        var other = events["other-flag"];
        missing.GetProperty("requestedType").GetString().ShouldBe("JsonElement");
        missing.GetProperty("usedDefault").GetBoolean().ShouldBeTrue();
        missing.GetProperty("defaultValue").TryGetProperty("value", out _).ShouldBeFalse();
        missing.GetProperty("defaultValue").GetProperty("valueId").GetString().ShouldNotBeNullOrEmpty();
        other.GetProperty("defaultValue").GetProperty("valueId").GetString()
            .ShouldNotBe(missing.GetProperty("defaultValue").GetProperty("valueId").GetString());
    }

    [Fact]
    public async Task MapsTheTargetingKeyNameAndTraitsOntoTheContextRulesAreEvaluatedAgainst()
    {
        var provider = await ReadyAsync(Bundles.Of(
            Bundles.Config("by-id", "string", "\"default\"", Bundles.Rule("identifier", null, "user-1", "matched")),
            Bundles.Config("by-name", "string", "\"default\"", Bundles.Rule("name", null, "Ada Lovelace", "matched")),
            Bundles.Config("by-trait", "string", "\"default\"", Bundles.Rule("traits", "/region", "EU", "matched"))));
        var matching = EvaluationContext.Builder()
            .SetTargetingKey("user-1")
            .Set("name", "Ada Lovelace")
            .Set("traits", Structure.Builder().Set("region", "EU").Build())
            .Build();
        var other = EvaluationContext.Builder().SetTargetingKey("user-2").Build();

        (await provider.ResolveStringValueAsync("by-id", "x", matching, Cancellation)).Value.ShouldBe("matched");
        (await provider.ResolveStringValueAsync("by-name", "x", matching, Cancellation)).Value.ShouldBe("matched");
        (await provider.ResolveStringValueAsync("by-trait", "x", matching, Cancellation)).Value.ShouldBe("matched");
        (await provider.ResolveStringValueAsync("by-id", "x", matching, Cancellation)).Variant.ShouldBe("rule-value");
        (await provider.ResolveStringValueAsync("by-id", "x", other, Cancellation)).Value.ShouldBe("default");
        (await provider.ResolveStringValueAsync("by-name", "x", other, Cancellation)).Value.ShouldBe("default");
        (await provider.ResolveStringValueAsync("by-trait", "x", other, Cancellation)).Value.ShouldBe("default");
    }

    [Fact]
    public async Task FallsBackToTheIdAttributeWhenThereIsNoTargetingKey()
    {
        var provider = await ReadyAsync(Bundles.Of(
            Bundles.Config("by-id", "string", "\"default\"", Bundles.Rule("identifier", null, "42", "matched"))));
        var withId = EvaluationContext.Builder().Set("id", 42).Build();
        var withBoth = EvaluationContext.Builder().SetTargetingKey("user-1").Set("id", 42).Build();

        (await provider.ResolveStringValueAsync("by-id", "x", withId, Cancellation)).Value.ShouldBe("matched");
        (await provider.ResolveStringValueAsync("by-id", "x", withBoth, Cancellation)).Value.ShouldBe("default");
    }

    [Fact]
    public async Task KeepsAnAnonymousContextOutOfTheReportedTelemetry()
    {
        var provider = await ReadyAsync(Bundles.Of(Bundles.Config("greeting", "string", "\"Bye\"")));
        var known = EvaluationContext.Builder().SetTargetingKey("known-user").Build();
        var hidden = EvaluationContext.Builder().SetTargetingKey("hidden-user").Set("anonymous", true).Build();

        await provider.ResolveStringValueAsync("greeting", "x", known, Cancellation);
        await provider.ResolveStringValueAsync("greeting", "x", hidden, Cancellation);
        await provider.ShutdownAsync(Cancellation);

        var telemetry = await TelemetryBodyAsync();
        telemetry.ShouldContain("known-user");
        telemetry.ShouldNotContain("hidden-user");
    }

    [Fact]
    public async Task IdentifiesItselfAsTheProviderWhenConnecting()
    {
        await ReadyAsync(Bundles.Of(Bundles.Config("greeting", "string", "\"Bye\"")));

        _server.UserAgents[0]!.ShouldMatch("^dotnet-openfeature-server-provider/\\S+$");
        _server.Bodies[0].ShouldContain("\"sdkName\":\"dotnet-openfeature-server-provider\"");
        _server.Bodies[0].ShouldMatch("\"sdkVersion\":\"[^\"]+\"");
    }

    [Fact]
    public async Task IdentifiesItselfAsTheProviderWhenReportingTelemetry()
    {
        var provider = await ReadyAsync(Bundles.Of(Bundles.Config("greeting", "string", "\"Bye\"")));

        await provider.ResolveStringValueAsync("greeting", "x", null, Cancellation);
        await provider.ShutdownAsync(Cancellation);

        (await TelemetryBodyAsync()).ShouldContain("\"sdkName\":\"dotnet-openfeature-server-provider\"");
        _server.UserAgents[_server.Paths.IndexOf("/server/telemetry/v1")]!.ShouldMatch("^dotnet-openfeature-server-provider/\\S+$");
    }

    [Fact]
    public async Task EmitsConfigurationChangedForTheFirstConfigStateAndForEveryUpdate()
    {
        var provider = await ReadyAsync(Bundles.Of(
            Bundles.Config("greeting", "string", "\"Bye\""),
            Bundles.Config("show-banner", "boolean", "true")));

        var initial = await NextEventAsync(provider);
        initial.Type.ShouldBe(ProviderEventTypes.ProviderConfigurationChanged);
        initial.ProviderName.ShouldBe("ConfigDirectorProvider");
        initial.FlagsChanged.ShouldBe(["greeting", "show-banner"]);

        _server.Push(Bundles.DeltaOf(Bundles.Config("greeting", "string", "\"Ciao\"")));

        var update = await NextEventAsync(provider);
        update.Type.ShouldBe(ProviderEventTypes.ProviderConfigurationChanged);
        update.FlagsChanged.ShouldBe(["greeting"]);
        (await provider.ResolveStringValueAsync("greeting", "Hello", null, Cancellation)).Value.ShouldBe("Ciao");
    }

    [Fact]
    public async Task FinishesInitializingWithoutConfigsAndBecomesReadyWhenTheyArrive()
    {
        _server.Bundle = Bundles.Of(Bundles.Config("show-banner", "boolean", "true"));
        _server.Replies(HttpStatusCode.BadGateway, "try again");
        var provider = Provider(TimeSpan.FromMilliseconds(200));

        await provider.InitializeAsync(EvaluationContext.Empty, Cancellation);

        var early = await provider.ResolveBooleanValueAsync("show-banner", false, null, Cancellation);
        early.Value.ShouldBeFalse();
        early.Reason.ShouldBe(Reason.Error);
        early.ErrorType.ShouldBe(ErrorType.ProviderNotReady);

        (await NextEventAsync(provider)).Type.ShouldBe(ProviderEventTypes.ProviderConfigurationChanged);
        (await NextEventAsync(provider)).Type.ShouldBe(ProviderEventTypes.ProviderReady);
        (await provider.ResolveBooleanValueAsync("show-banner", false, null, Cancellation)).Value.ShouldBeTrue();
    }

    [Fact]
    public async Task ClosesTheClientWhenOpenFeatureShutsTheProviderDown()
    {
        var provider = await ReadyAsync(Bundles.Of(Bundles.Config("show-banner", "boolean", "true")));
        (await provider.ResolveBooleanValueAsync("show-banner", false, null, Cancellation)).Value.ShouldBeTrue();

        await provider.ShutdownAsync(Cancellation);

        var details = await provider.ResolveBooleanValueAsync("show-banner", false, null, Cancellation);
        details.Value.ShouldBeFalse();
        details.Reason.ShouldBe(Reason.Error);
        details.ErrorType.ShouldBe(ErrorType.ProviderFatal);
        details.ErrorMessage.ShouldBe("The ConfigDirector client has been closed.");
    }

    [Fact]
    public async Task ClosesTheClientWhenDisposed()
    {
        var provider = await ReadyAsync(Bundles.Of(Bundles.Config("show-banner", "boolean", "true")));

        provider.Dispose();

        (await provider.ResolveBooleanValueAsync("show-banner", false, null, Cancellation)).Value.ShouldBeFalse();
    }

    [Fact]
    public void NamesItselfInTheProviderMetadata() =>
        Provider().GetMetadata().Name.ShouldBe("ConfigDirectorProvider");

    [Fact]
    public void RejectsAMissingServerSdkKey()
    {
        Should.Throw<ArgumentNullException>(() => new ConfigDirectorProvider(null!));
        Should.Throw<ArgumentException>(() => new ConfigDirectorProvider(" "));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        _server.Dispose();
    }

    private ConfigDirectorProvider Provider(TimeSpan? timeout = null)
    {
        var options = new ConfigDirectorClientOptions { Connection = { Timeout = timeout ?? Timeout } };
        _server.Attach(options);
        var provider = new ConfigDirectorProvider("sdk-key", options);
        _providers.Add(provider);
        return provider;
    }

    private async Task<ConfigDirectorProvider> ReadyAsync(string bundle)
    {
        _server.Bundle = bundle;
        var provider = Provider();
        await provider.InitializeAsync(EvaluationContext.Empty, Cancellation);
        return provider;
    }

    private static async Task<ProviderEventPayload> NextEventAsync(ConfigDirectorProvider provider)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        deadline.CancelAfter(Timeout);
        return (ProviderEventPayload)await provider.GetEventChannel().Reader.ReadAsync(deadline.Token);
    }

    private async Task<string> TelemetryBodyAsync()
    {
        for (var attempt = 0; attempt < 500 && !_server.Paths.Contains("/server/telemetry/v1"); attempt++)
        {
            await Task.Delay(10, Cancellation);
        }

        var index = _server.Paths.IndexOf("/server/telemetry/v1");
        index.ShouldBeGreaterThanOrEqualTo(0, "no telemetry report arrived");
        return _server.Bodies[index];
    }
}
