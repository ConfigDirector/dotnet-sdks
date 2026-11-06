using System.Text.Json;

namespace ConfigDirector.Tests.Integration;

public sealed class ClientVariationTests : IDisposable
{
    private static readonly Context InTheControlShare = new() { Id = "user-1" };

    private static readonly Context InTheTreatmentShare = new() { Id = "u582" };

    private static readonly Variation Control = new() { Id = "variation-control", Name = "Control" };

    private static readonly Variation Treatment = new() { Id = "variation-treatment", Name = "Treatment" };

    private static readonly JsonSerializerOptions ClientPayloadJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SdkServer _server = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EvaluateReturnsTheValueReasonValueIdAndVariationARolloutServed()
    {
        await using var client = await ReadyClientAsync();

        var evaluation = client.Evaluate("checkout-experiment", "in-code", InTheTreatmentShare);

        evaluation.Key.ShouldBe("checkout-experiment");
        evaluation.Value.ShouldBe("treatment");
        evaluation.IsDefault.ShouldBeFalse();
        evaluation.Reason.ShouldBe(EvaluationReason.FoundMatch);
        evaluation.ValueId.ShouldBe("value-treatment");
        evaluation.Variation.ShouldBe(Treatment);
        evaluation.Context.ShouldBe(InTheTreatmentShare);
    }

    [Fact]
    public async Task EvaluateReturnsTheVariationOfARuleServingOneValue()
    {
        await using var client = await ReadyClientAsync();

        var evaluation = client.Evaluate("greeting-by-plan", "in-code", new Context { Id = "user-1", Traits = { ["plan"] = "pro" } });

        evaluation.Value.ShouldBe("welcome");
        evaluation.Variation.ShouldBe(new Variation { Id = "variation-welcome", Name = "Welcome" });
    }

    [Fact]
    public async Task EvaluateReturnsTheVariationOfTheDefaultTargetingRuleValue()
    {
        await using var client = await ReadyClientAsync();

        var evaluation = client.Evaluate("greeting-by-plan", "in-code", new Context { Id = "user-1" });

        evaluation.Value.ShouldBe("hello");
        evaluation.Reason.ShouldBe(EvaluationReason.FoundMatch);
        evaluation.Variation.ShouldBe(new Variation { Id = "variation-hello", Name = "Hello" });
    }

    [Fact]
    public async Task EvaluateReturnsAnUnnamedVariationWithoutAName()
    {
        await using var client = await ReadyClientAsync();

        var evaluation = client.Evaluate("unnamed-experiment", "in-code");

        evaluation.Variation.ShouldBe(new Variation { Id = "variation-unnamed", Name = null });
    }

    [Fact]
    public async Task EvaluateReturnsNoVariationForAConfigWithoutVariations()
    {
        await using var client = await ReadyClientAsync();

        var evaluation = client.Evaluate("rolled-out-flag", false, InTheControlShare);

        evaluation.Value.ShouldBe(true);
        evaluation.ValueId.ShouldBe("value-on");
        evaluation.Variation.ShouldBeNull();
    }

    [Fact]
    public async Task EvaluateReadsAnInteger()
    {
        await using var client = await ReadyClientAsync();

        client.Evaluate("whole-number", 0).Value.ShouldBe(26);
    }

    [Fact]
    public async Task EvaluateReadsALong()
    {
        await using var client = await ReadyClientAsync();

        client.Evaluate("whole-number", 0L).Value.ShouldBe(26L);
    }

    [Fact]
    public async Task EvaluateReadsADouble()
    {
        await using var client = await ReadyClientAsync();

        client.Evaluate("ratio", 0.0).Value.ShouldBe(2.5);
    }

    [Fact]
    public async Task EvaluateReadsAFloat()
    {
        await using var client = await ReadyClientAsync();

        client.Evaluate("ratio", 0.0f).Value.ShouldBe(2.5f);
    }

    [Fact]
    public async Task EvaluateReadsADecimal()
    {
        await using var client = await ReadyClientAsync();

        client.Evaluate("ratio", 0.0m).Value.ShouldBe(2.5m);
    }

    [Fact]
    public async Task EvaluateReadsABoolean()
    {
        await using var client = await ReadyClientAsync();

        client.Evaluate("rolled-out-flag", false, InTheControlShare).Value.ShouldBe(true);
    }

    [Fact]
    public async Task EvaluateReadsText()
    {
        await using var client = await ReadyClientAsync();

        client.Evaluate("checkout-experiment", "in-code", InTheControlShare).Value.ShouldBe("control");
    }

    [Fact]
    public async Task EvaluateReadsJson()
    {
        await using var client = await ReadyClientAsync();

        var value = (JsonElement)client.Evaluate("settings", default(JsonElement)).Value;

        value.GetProperty("name").GetString().ShouldBe("checkout");
    }

    [Fact]
    public async Task EvaluateJsonBindsTheValueToTheRequestedType()
    {
        await using var client = await ReadyClientAsync();

        var evaluation = client.EvaluateJson("settings", new CheckoutSettings { Name = "in-code" });

        evaluation.Value.ShouldBe(new CheckoutSettings { Name = "checkout" });
        evaluation.Reason.ShouldBe(EvaluationReason.FoundMatch);
        evaluation.Variation.ShouldBe(new Variation { Id = "variation-settings", Name = "Settings" });
    }

    [Fact]
    public async Task EvaluateJsonReturnsTheInCodeDefaultValueWithNoVariationWhenTheValueCannotBeBound()
    {
        await using var client = await ReadyClientAsync();
        var inCode = new CheckoutSettings { Name = "in-code" };

        var evaluation = client.EvaluateJson("checkout-experiment", inCode, InTheControlShare);

        evaluation.Value.ShouldBeSameAs(inCode);
        evaluation.IsDefault.ShouldBeTrue();
        evaluation.Reason.ShouldBe(EvaluationReason.InvalidJson);
        evaluation.Variation.ShouldBeNull();
    }

    [Fact]
    public async Task EvaluateReturnsTheInCodeDefaultValueWithNoVariationWhenTheValueCannotBeRead()
    {
        await using var client = await ReadyClientAsync();

        var evaluation = client.Evaluate("checkout-experiment", 7, InTheTreatmentShare);

        evaluation.Value.ShouldBe(7);
        evaluation.IsDefault.ShouldBeTrue();
        evaluation.Reason.ShouldBe(EvaluationReason.InvalidNumber);
        evaluation.ValueId.ShouldBeNull();
        evaluation.Variation.ShouldBeNull();
    }

    [Fact]
    public async Task EvaluateReturnsTheInCodeDefaultValueForAConfigTheServerDidNotSend()
    {
        await using var client = await ReadyClientAsync();

        var evaluation = client.Evaluate("no-such-config", "in-code", InTheControlShare);

        evaluation.Key.ShouldBe("no-such-config");
        evaluation.Value.ShouldBe("in-code");
        evaluation.IsDefault.ShouldBeTrue();
        evaluation.Reason.ShouldBe(EvaluationReason.ConfigStateMissing);
        evaluation.ValueId.ShouldBeNull();
        evaluation.Variation.ShouldBeNull();
        evaluation.Context.ShouldBe(InTheControlShare);
    }

    [Fact]
    public async Task EvaluateReturnsTheInCodeDefaultValueBeforeConfigStateArrives()
    {
        await using var client = Client();

        var evaluation = client.Evaluate("checkout-experiment", "in-code", InTheControlShare);

        evaluation.Value.ShouldBe("in-code");
        evaluation.IsDefault.ShouldBeTrue();
        evaluation.Reason.ShouldBe(EvaluationReason.ClientNotReady);
    }

    [Fact]
    public async Task EvaluateReturnsTheInCodeDefaultValueOnceTheClientIsClosed()
    {
        var client = await ReadyClientAsync();
        await client.DisposeAsync();

        var evaluation = client.Evaluate("checkout-experiment", "in-code", InTheControlShare);

        evaluation.Value.ShouldBe("in-code");
        evaluation.IsDefault.ShouldBeTrue();
        evaluation.Reason.ShouldBe(EvaluationReason.ClientNotReady);
        evaluation.Variation.ShouldBeNull();
    }

    [Fact]
    public async Task EvaluateRejectsAMissingKey()
    {
        await using var client = await ReadyClientAsync();

        Should.Throw<ArgumentNullException>(() => client.Evaluate(null!, "in-code"));
    }

    [Fact]
    public async Task EvaluateRejectsABlankKey()
    {
        await using var client = await ReadyClientAsync();

        Should.Throw<ArgumentException>(() => client.Evaluate(" ", "in-code"));
    }

    [Fact]
    public async Task EvaluateRejectsAMissingInCodeDefaultValue()
    {
        await using var client = await ReadyClientAsync();

        Should.Throw<ArgumentNullException>(() => client.Evaluate("checkout-experiment", (string)null!));
    }

    [Fact]
    public async Task EvaluateJsonRejectsAMissingInCodeDefaultValue()
    {
        await using var client = await ReadyClientAsync();

        Should.Throw<ArgumentNullException>(() => client.EvaluateJson("settings", (CheckoutSettings)null!));
    }

    [Fact]
    public async Task EvaluateRaisesConfigEvaluatedWithTheEvaluationItReturns()
    {
        await using var client = await ReadyClientAsync();
        var raised = EvaluationsOf(client);

        var evaluation = client.Evaluate("checkout-experiment", "in-code", InTheTreatmentShare);

        raised.ShouldHaveSingleItem().ShouldBe(evaluation);
    }

    [Fact]
    public async Task GetValueRaisesConfigEvaluatedWithTheVariation()
    {
        await using var client = await ReadyClientAsync();
        var raised = EvaluationsOf(client);

        client.GetValue("checkout-experiment", "in-code", InTheTreatmentShare);

        raised.ShouldHaveSingleItem().Variation.ShouldBe(Treatment);
    }

    [Fact]
    public async Task ConfigEvaluatedCarriesNoVariationForAConfigWithoutVariations()
    {
        await using var client = await ReadyClientAsync();
        var raised = EvaluationsOf(client);

        client.GetValue("rolled-out-flag", false, InTheControlShare);

        raised.ShouldHaveSingleItem().Variation.ShouldBeNull();
    }

    [Fact]
    public async Task GetAllConfigsCarriesTheRolloutThatServedEachValueAndItsVariation()
    {
        await using var client = await ReadyClientAsync();

        var state = client.GetAllConfigs(InTheTreatmentShare)["checkout-experiment"];

        state.Value.ShouldBe("treatment");
        state.Variation.ShouldBe(Treatment);
        state.RuleId.ShouldBe("rule-rollout");
        state.ShareId.ShouldBe("share-treatment");
    }

    [Fact]
    public async Task GetAllConfigsCarriesTheVariationAloneWhenNoRolloutServedTheValue()
    {
        await using var client = await ReadyClientAsync();

        var state = client.GetAllConfigs(InTheControlShare)["greeting-by-plan"];

        state.Variation.ShouldBe(new Variation { Id = "variation-hello", Name = "Hello" });
        state.RuleId.ShouldBeNull();
        state.ShareId.ShouldBeNull();
    }

    [Fact]
    public async Task AStateHandedToAClientSdkNamesItsVariationRuleAndShareInTheClientPayloadsSpelling()
    {
        await using var client = await ReadyClientAsync();
        var state = client.GetAllConfigs(InTheTreatmentShare)["checkout-experiment"];

        var written = JsonDocument.Parse(JsonSerializer.Serialize(state, ClientPayloadJsonOptions)).RootElement;

        written.GetProperty("variation").GetProperty("id").GetString().ShouldBe("variation-treatment");
        written.GetProperty("variation").GetProperty("name").GetString().ShouldBe("Treatment");
        written.GetProperty("ruleId").GetString().ShouldBe("rule-rollout");
        written.GetProperty("shareId").GetString().ShouldBe("share-treatment");
    }

    [Fact]
    public async Task GetValueServesTheDefaultTargetingRuleValueToAContextWithoutAnId()
    {
        await using var client = await ReadyClientAsync();

        client.GetValue("rolled-out-flag", true, new Context { Name = "Ada" }).ShouldBeFalse();
    }

    [Fact]
    public async Task EvaluateReportsASkippedRolloutForAContextWithoutAnId()
    {
        await using var client = await ReadyClientAsync();

        var evaluation = client.Evaluate("checkout-experiment", "in-code", new Context { Name = "Ada" });

        evaluation.Value.ShouldBe("control");
        evaluation.IsDefault.ShouldBeFalse();
        evaluation.Reason.ShouldBe(EvaluationReason.RolloutSkippedNoIdentifier);
        evaluation.ValueId.ShouldBe("value-control");
        evaluation.Variation.ShouldBe(Control);
    }

    [Fact]
    public async Task EvaluateReportsASkippedRolloutWithoutAContext()
    {
        await using var client = await ReadyClientAsync();

        var evaluation = client.Evaluate("checkout-experiment", "in-code");

        evaluation.Value.ShouldBe("control");
        evaluation.Reason.ShouldBe(EvaluationReason.RolloutSkippedNoIdentifier);
    }

    [Fact]
    public async Task EvaluateReportsASkippedRolloutForAContextIgnoredForAnInvalidKind()
    {
        await using var client = await ReadyClientAsync();

        var evaluation = client.Evaluate("checkout-experiment", "in-code", new Context { Id = "u582", Kind = "Device" });

        evaluation.Value.ShouldBe("control");
        evaluation.Reason.ShouldBe(EvaluationReason.RolloutSkippedNoIdentifier);
        evaluation.Context.ShouldBeNull();
    }

    [Fact]
    public async Task EvaluateReturnsTheInCodeDefaultValueWhenTheValueOfASkippedRolloutCannotBeRead()
    {
        await using var client = await ReadyClientAsync();

        var evaluation = client.Evaluate("checkout-experiment", 7, new Context { Name = "Ada" });

        evaluation.Value.ShouldBe(7);
        evaluation.IsDefault.ShouldBeTrue();
        evaluation.Reason.ShouldBe(EvaluationReason.InvalidNumber);
        evaluation.Variation.ShouldBeNull();
    }

    [Fact]
    public async Task ConfigEvaluatedReportsASkippedRollout()
    {
        await using var client = await ReadyClientAsync();
        var raised = EvaluationsOf(client);

        client.GetValue("checkout-experiment", "in-code", new Context { Name = "Ada" });

        var evaluation = raised.ShouldHaveSingleItem();
        evaluation.Reason.ShouldBe(EvaluationReason.RolloutSkippedNoIdentifier);
        evaluation.IsDefault.ShouldBeFalse();
        evaluation.Variation.ShouldBe(Control);
    }

    [Fact]
    public async Task AWatchReceivesTheDefaultTargetingRuleValueForAContextWithoutAnId()
    {
        _server.Bundle = SampleConfigs.Variations;
        await using var client = Client();
        var received = new List<bool>();
        client.Watch("rolled-out-flag", true, received.Add, new Context { Name = "Ada" });

        await client.InitializeAsync(Cancellation);

        received.ShouldBe([false]);
    }

    [Fact]
    public async Task GetAllConfigsServesTheDefaultTargetingRuleValueToAContextWithoutAnId()
    {
        await using var client = await ReadyClientAsync();

        var state = client.GetAllConfigs(new Context { Name = "Ada" })["checkout-experiment"];

        state.Value.ShouldBe("control");
        state.ValueId.ShouldBe("value-control");
        state.Variation.ShouldBe(Control);
        state.RuleId.ShouldBeNull();
        state.ShareId.ShouldBeNull();
    }

    public void Dispose() => _server.Dispose();

    private async Task<ConfigDirectorClient> ReadyClientAsync()
    {
        _server.Bundle = SampleConfigs.Variations;
        var client = Client();
        await client.InitializeAsync(Cancellation);
        return client;
    }

    private ConfigDirectorClient Client()
    {
        var options = new ConfigDirectorClientOptions();
        _server.Attach(options);
        options.Telemetry.FlushInterval = TimeSpan.FromMinutes(5);
        return new ConfigDirectorClient("sdk-key", options);
    }

    private static List<ConfigEvaluation> EvaluationsOf(ConfigDirectorClient client)
    {
        var raised = new List<ConfigEvaluation>();
        client.ConfigEvaluated += (_, evaluated) => raised.Add(evaluated.Evaluation);
        return raised;
    }

    private sealed record CheckoutSettings
    {
        public string? Name { get; init; }
    }
}
