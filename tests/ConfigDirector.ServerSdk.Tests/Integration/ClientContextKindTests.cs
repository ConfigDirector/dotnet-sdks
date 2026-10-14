using System.Net;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Tests.Integration;

public sealed class ClientContextKindTests : IDisposable
{
    private const string SixtyFiveCharacters = "a-kind-of-sixty-five-characters-is-one-character-over-the-limit-x";

    private readonly SdkServer _server = new();

    public static TheoryData<string> InvalidKinds => ["", SixtyFiveCharacters, "Device", " device"];

    [Theory]
    [MemberData(nameof(InvalidKinds))]
    public async Task EvaluatesAContextWithAnInvalidKindAsIfNoContextWasSupplied(string kind)
    {
        _server.Bundle = Bundle;
        var logging = new CapturingLoggerFactory();
        await using var client = Client(logging);
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        var evaluations = new List<ConfigEvaluation>();
        client.ConfigEvaluated += (_, evaluated) => evaluations.Add(evaluated.Evaluation);

        var withInvalidKind = Should.NotThrow(
            () => client.GetValue("greeting", "in-code", new Context { Id = "user-1", Kind = kind }));
        client.GetValue("greeting", "in-code", new Context { Id = "user-1", Kind = kind });
        var withoutContext = client.GetValue("greeting", "in-code");

        withInvalidKind.ShouldBe("hello");
        withoutContext.ShouldBe("hello");
        evaluations[0].Reason.ShouldBe(evaluations[2].Reason);
        evaluations[0].ValueId.ShouldBe(evaluations[2].ValueId);
        evaluations[0].Context.ShouldBeNull();
        var logged = InvalidKindEntries(logging).ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Error);
        logged.EventId.Name.ShouldBe("InvalidContextKind");
        logged.Message.ShouldContain($"context kind {kind} and evaluated");
    }

    [Fact]
    public async Task EvaluatesKindConditionsAgainstTheEffectiveKind()
    {
        _server.Bundle = Bundle;
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetValue("greeting", "in-code", new Context { Id = "user-1", Kind = "device" }).ShouldBe("hi device");
        client.GetValue("greeting", "in-code", new Context { Id = "user-1", Kind = "user" }).ShouldBe("hi user-1");
        client.GetValue("greeting", "in-code", new Context { Id = "user-1" }).ShouldBe("hi user-1");
        client.GetValue("greeting", "in-code", new Context { Id = "user-1", Kind = new string('z', 64) })
            .ShouldBe("hi user-1");
        client.GetValue("greeting", "in-code", new Context { Kind = "device" }).ShouldBe("hi device");
    }

    [Fact]
    public async Task AssignsUserAndDeviceContextsSharingAnIdentifierToTheirOwnBuckets()
    {
        _server.Bundle = Bundle;
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetValue("rollout", "in-code", new Context { Id = "user-1" }).ShouldBe("0");
        client.GetValue("rollout", "in-code", new Context { Id = "user-1", Kind = "user" }).ShouldBe("0");
        client.GetValue("rollout", "in-code", new Context { Id = "user-1", Kind = "device" }).ShouldBe("5");
        client.GetValue("rollout", "in-code", new Context { Kind = "device" }).ShouldBe("0");
    }

    [Fact]
    public async Task EvaluatesEveryConfigAsIfNoContextWasSuppliedForAnInvalidKind()
    {
        _server.Bundle = Bundle;
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        var states = client.GetAllConfigs(new Context { Id = "user-1", Kind = "Device" });

        states["greeting"].Value.ShouldBe("hello");
        states["greeting"].ValueId.ShouldBe("dv-greeting");
        states["rollout"].Value.ShouldBe("0");
    }

    [Fact]
    public async Task NotifiesAWatchOfTheValueForNoContextWhenItsContextHasAnInvalidKind()
    {
        _server.Bundle = Bundle;
        await using var client = Client();
        var received = new List<string>();
        client.Watch("greeting", "in-code", received.Add, new Context { Id = "user-1", Kind = "Device" });

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        received.ShouldBe(["hello"]);
    }

    [Fact]
    public async Task LogsEachInvalidKindOnceAtErrorLevel()
    {
        _server.Bundle = Bundle;
        var logging = new CapturingLoggerFactory();
        await using var client = Client(logging);
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetValue("greeting", "in-code", new Context { Id = "user-1", Kind = "Device" });
        client.GetValue("rollout", "in-code", new Context { Id = "user-2", Kind = "Device" });
        client.GetAllConfigs(new Context { Kind = "Device" });
        client.GetValue("greeting", "in-code", new Context { Kind = "Tenant" });

        var logged = InvalidKindEntries(logging);
        logged.Length.ShouldBe(2);
        logged.ShouldAllBe(entry => entry.Level == LogLevel.Error && entry.EventId.Name == "InvalidContextKind");
        logged[0].Message.ShouldBe(
            "Ignored a context with the invalid context kind Device and evaluated as if no context was supplied. "
            + "A context kind has 1 to 64 characters, each a lowercase ASCII letter, a digit, '-' or '_'.");
        logged[1].Message.ShouldContain("context kind Tenant and");
    }

    [Fact]
    public async Task LogsTheFirst128CharactersOfAnInvalidKind()
    {
        _server.Bundle = Bundle;
        var logging = new CapturingLoggerFactory();
        await using var client = Client(logging);
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        var kind = new string('k', 128) + "-and-beyond";

        client.GetValue("greeting", "in-code", new Context { Kind = kind });

        InvalidKindEntries(logging).ShouldHaveSingleItem().Message
            .ShouldContain($"context kind {new string('k', 128)} and evaluated");
    }

    [Fact]
    public async Task SaysOnceThatFurtherKindsAreNotLoggedAfterTwentyDistinctKinds()
    {
        _server.Bundle = Bundle;
        var logging = new CapturingLoggerFactory();
        await using var client = Client(logging);
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        for (var index = 0; index < 23; index++)
        {
            client.GetValue("greeting", "in-code", new Context { Kind = $"Kind-{index}" });
        }

        client.GetValue("greeting", "in-code", new Context { Kind = "Kind-0" });

        InvalidKindEntries(logging).Length.ShouldBe(20);
        var notLogged = logging.Logger.Entries.Where(entry => entry.EventId.Id == 10).ShouldHaveSingleItem();
        notLogged.Level.ShouldBe(LogLevel.Error);
        notLogged.EventId.Name.ShouldBe("InvalidContextKindsNotLogged");
        notLogged.Message.ShouldBe(
            "Ignored a context with another invalid context kind and evaluated as if no context was supplied. "
            + "Further invalid context kinds are not logged individually until the next telemetry report.");
    }

    [Fact]
    public async Task LogsAKindAgainOnceTheTelemetryReportForItsIntervalWasSent()
    {
        _server.Bundle = Bundle;
        var logging = new CapturingLoggerFactory();
        await using var client = Client(logging, flushInterval: TimeSpan.FromMilliseconds(50));
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetValue("greeting", "in-code", new Context { Kind = "Device" });
        await WaitAsync(() => _server.Paths.Contains("/server/telemetry/v1"));
        client.GetValue("greeting", "in-code", new Context { Kind = "Device" });

        InvalidKindEntries(logging).Length.ShouldBe(2);
    }

    [Fact]
    public async Task LogsEveryInvalidKindOnceTheClientIsClosed()
    {
        _server.Bundle = Bundle;
        var logging = new CapturingLoggerFactory();
        var client = Client(logging);
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        await client.DisposeAsync();

        client.GetValue("greeting", "in-code", new Context { Kind = "Device" });
        client.GetValue("greeting", "in-code", new Context { Kind = "Device" });

        InvalidKindEntries(logging).Length.ShouldBe(2);
    }

    [Fact]
    public async Task LogsEveryInvalidKindOnceTelemetryWasRejected()
    {
        _server.Bundle = Bundle;
        var logging = new CapturingLoggerFactory();
        await using var client = Client(logging, flushInterval: TimeSpan.FromMilliseconds(50));
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        _server.Replies(HttpStatusCode.Forbidden);

        client.GetValue("greeting", "in-code", new Context { Kind = "Device" });
        await WaitAsync(() => logging.Logger.Entries.Any(entry => entry.Message.Contains("No longer collecting")));
        client.GetValue("greeting", "in-code", new Context { Kind = "Device" });
        client.GetValue("greeting", "in-code", new Context { Kind = "Device" });

        InvalidKindEntries(logging).Length.ShouldBe(3);
    }

    public void Dispose() => _server.Dispose();

    private static async Task WaitAsync(Func<bool> until)
    {
        for (var attempt = 0; attempt < 500 && !until(); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        until().ShouldBeTrue();
    }

    private static (LogLevel Level, EventId EventId, string Message, Exception? Error)[] InvalidKindEntries(
        CapturingLoggerFactory logging) =>
        [.. logging.Logger.Entries.Where(entry => entry.EventId.Id == 9)];

    private const string Bundle = """
        {"kind": "full", "timestamp": "2026-08-01T12:00:00.000Z", "configs": {
          "greeting": {"id": "id-greeting", "key": "greeting", "type": "string", "target": {
            "defaultValue": "hello", "defaultValueId": "dv-greeting", "rules": [
              {"id": "r-device", "type": "conditional", "order": 1, "value": "hi device", "valueId": "rv-device",
               "conditions": [{"id": "c-device", "kind": "attribute", "attribute": "kind", "operator": "is one of",
                               "targetType": "text", "targetValues": ["device"]}]},
              {"id": "r-user-1", "type": "conditional", "order": 2, "value": "hi user-1", "valueId": "rv-user-1",
               "conditions": [{"id": "c-user-1", "kind": "attribute", "attribute": "identifier", "operator": "=",
                               "targetType": "text", "targetValues": ["user-1"]}]}]}},
          "rollout": {"id": "11111111-1111-4111-8111-111111111111", "key": "rollout", "type": "string", "target": {
            "defaultValue": "none", "rules": [
              {"id": "r-rollout", "type": "percentage", "order": 1, "percentages": [
                {"id": "b0", "percentage": 10, "value": "0"}, {"id": "b1", "percentage": 10, "value": "1"},
                {"id": "b2", "percentage": 10, "value": "2"}, {"id": "b3", "percentage": 10, "value": "3"},
                {"id": "b4", "percentage": 10, "value": "4"}, {"id": "b5", "percentage": 10, "value": "5"},
                {"id": "b6", "percentage": 10, "value": "6"}, {"id": "b7", "percentage": 10, "value": "7"},
                {"id": "b8", "percentage": 10, "value": "8"}, {"id": "b9", "percentage": 10, "value": "9"}]}]}}}}
        """;

    private ConfigDirectorClient Client(CapturingLoggerFactory? loggerFactory = null, TimeSpan? flushInterval = null)
    {
        var options = new ConfigDirectorClientOptions();
        _server.Attach(options);
        if (loggerFactory is not null)
        {
            options.LoggerFactory = loggerFactory;
        }

        options.Telemetry.FlushInterval = flushInterval ?? TimeSpan.FromMinutes(5);
        return new ConfigDirectorClient("sdk-key", options);
    }
}
