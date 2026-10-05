namespace ConfigDirector.Tests.Integration;

public sealed class ClientSegmentTests : IDisposable
{
    private const string Acme = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string Beta = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";

    private static readonly Context Member = new() { Id = "10", Traits = { ["email"] = "ann@acme.com" } };
    private static readonly Context Outsider = new() { Id = "20", Traits = { ["email"] = "bob@other.com" } };

    private readonly SdkServer _server = new();

    [Fact]
    public async Task EvaluatesASegmentConditionAgainstTheSegmentsAFullSetCarries()
    {
        _server.Bundle = SetOf("full", GreetingForMembers("bonjour"), MembersOf(Acme, "@acme.com"));
        await using var client = Client();

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetValue("greeting", "in-code-default", Member).ShouldBe("bonjour");
        client.GetValue("greeting", "in-code-default", Outsider).ShouldBe("hello");
        client.GetAllConfigs(Member)["greeting"].Value.ShouldBe("bonjour");
    }

    [Fact]
    public async Task KeepsTheSegmentsItHoldsAcrossADeltaThatCarriesNone()
    {
        _server.Bundle = SetOf("full", GreetingForMembers("bonjour"), MembersOf(Acme, "@acme.com"));
        await using var client = Client();
        var received = new List<string>();
        client.Watch("greeting", "in-code-default", received.Add, Member);

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        _server.Push(SetOf("delta", GreetingForMembers("salut"), "{}"));
        await WaitAsync(() => received.Count == 2);

        received.ShouldBe(["bonjour", "salut"]);
        client.GetValue("greeting", "in-code-default", Member).ShouldBe("salut");
    }

    [Fact]
    public async Task AddsTheSegmentsADeltaCarriesToTheOnesItHolds()
    {
        _server.Bundle = SetOf("full", GreetingForMembers("bonjour"), MembersOf(Acme, "@acme.com"));
        await using var client = Client();
        var betaMember = new Context { Id = "30", Traits = { ["email"] = "cat@beta.com" } };

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        _server.Push(SetOf("delta", FarewellForMembersOf(Beta, "ciao"), MembersOf(Beta, "@beta.com")));
        await WaitAsync(() => client.GetValue("farewell", "in-code-default", betaMember) == "ciao");

        client.GetValue("greeting", "in-code-default", Member).ShouldBe("bonjour");
    }

    [Fact]
    public async Task DropsTheSegmentsAFullSetNoLongerCarries()
    {
        _server.Bundle = SetOf("full", GreetingForMembers("bonjour"), MembersOf(Acme, "@acme.com"));
        await using var client = Client();
        var received = new List<string>();
        client.Watch("greeting", "in-code-default", received.Add, Member);

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        _server.Push(SetOf("full", GreetingForMembers("salut"), null));
        await WaitAsync(() => received.Count == 2);

        received.ShouldBe(["bonjour", "hello"]);
        client.GetValue("greeting", "in-code-default", Member).ShouldBe("hello");
    }

    [Fact]
    public async Task CallsTheWatchersOfTheConfigsWhoseRulesUseASegmentADeltaCarriesWithoutThem()
    {
        _server.Bundle = SetOf(
            "full",
            $"{GreetingForMembers("bonjour")}, {FarewellForMembersOf(Beta, "ciao")}",
            MembersOf(Acme, "@acme.com"));
        await using var client = Client();
        var updates = new List<ConfigsUpdatedEventArgs>();
        var greetings = new List<string>();
        var farewells = new List<string>();
        client.ConfigsUpdated += (_, updated) => updates.Add(updated);
        client.Watch("greeting", "in-code-default", greetings.Add, Member);
        client.Watch("farewell", "in-code-default", farewells.Add, Member);

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        _server.Push(SetOf("delta", string.Empty, MembersOf(Acme, "@other.com")));
        await WaitAsync(() => updates.Count == 2);

        updates[1].Keys.ShouldBe(["greeting"]);
        updates[1].RemovedKeys.ShouldBeEmpty();
        greetings.ShouldBe(["bonjour", "hello"]);
        farewells.ShouldBe(["bye"]);
    }

    [Fact]
    public async Task ListsAConfigOnceWhenADeltaCarriesItWithASegmentItsRulesUse()
    {
        _server.Bundle = SetOf("full", GreetingForMembers("bonjour"), MembersOf(Acme, "@acme.com"));
        await using var client = Client();
        var updates = new List<ConfigsUpdatedEventArgs>();
        var greetings = new List<string>();
        client.ConfigsUpdated += (_, updated) => updates.Add(updated);
        client.Watch("greeting", "in-code-default", greetings.Add, Member);

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        _server.Push(SetOf("delta", GreetingForMembers("salut"), MembersOf(Acme, "@acme.com")));
        await WaitAsync(() => updates.Count == 2);

        updates[1].Keys.ShouldBe(["greeting"]);
        greetings.ShouldBe(["bonjour", "salut"]);
    }

    private static string MembersOf(string segmentId, string domain) =>
        $$$$"""
        {"{{{{segmentId}}}}": {"groups": [[{"id": "g0c0", "kind": "attribute", "attribute": "traits", "trait": "/email",
          "operator": "ends with any of", "targetType": "text", "targetValues": ["{{{{domain}}}}"]}]]}}
        """;

    private static string GreetingForMembers(string value) =>
        $$$"""
        "greeting": {"id": "id-greeting", "key": "greeting", "type": "string", "target": {"defaultValue": "hello",
          "defaultValueId": "dv-greeting", "rules": [{"id": "r-greeting", "type": "conditional", "order": 1, "target": "value",
          "value": "{{{value}}}", "valueId": "rv-greeting", "conditions": [{"id": "c-greeting", "kind": "segment",
          "operator": "in", "segmentId": "{{{Acme}}}"}]}]}}
        """;

    private static string FarewellForMembersOf(string segmentId, string value) =>
        $$$"""
        "farewell": {"id": "id-farewell", "key": "farewell", "type": "string", "target": {"defaultValue": "bye",
          "rules": [{"id": "r-farewell", "type": "conditional", "order": 1, "target": "value", "value": "{{{value}}}",
          "conditions": [{"id": "c-farewell", "kind": "segment", "operator": "in", "segmentId": "{{{segmentId}}}"}]}]}}
        """;

    private static string SetOf(string kind, string configsJson, string? segmentsJson)
    {
        var segments = segmentsJson is null ? string.Empty : $", \"segments\": {segmentsJson}";
        return $$$$"""{"kind": "{{{{kind}}}}", "timestamp": "2026-08-01T12:00:00.000Z", "configs": {{{{{configsJson}}}}}{{{{segments}}}}}""";
    }

    private ConfigDirectorClient Client()
    {
        var settings = new ConfigDirectorClientOptions();
        _server.Attach(settings);
        return new ConfigDirectorClient("server-sdk-key", settings);
    }

    private static async Task WaitAsync(Func<bool> until)
    {
        for (var attempt = 0; attempt < 300 && !until(); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        until().ShouldBeTrue();
    }

    public void Dispose() => _server.Dispose();
}
