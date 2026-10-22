using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Tests.Integration;

public sealed class OfflineModeTests : IDisposable
{
    private static readonly JsonElement EmptyJson = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly SdkServer _server = new();
    private readonly List<string> _files = [];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ServesInlineValuesWithTheConfigTypeTheirDotNetTypeImplies()
    {
        await using var client = new ConfigDirectorClient(Offline(values: new Dictionary<string, object>
        {
            ["new-checkout"] = true,
            ["max-items"] = 20,
            ["discount-rate"] = 0.15,
            ["greeting"] = "Bye",
            ["settings"] = new Dictionary<string, object> { ["theme"] = "dark" },
        }));

        await client.InitializeAsync(Cancellation);

        client.GetValue("new-checkout", false).ShouldBeTrue();
        client.GetValue("max-items", 0).ShouldBe(20);
        client.GetValue("discount-rate", 0.0).ShouldBe(0.15);
        client.GetValue("greeting", "Hello").ShouldBe("Bye");
        client.GetValue("settings", EmptyJson).GetProperty("theme").GetString().ShouldBe("dark");
        var configs = client.GetAllConfigs();
        configs["new-checkout"].Type.ShouldBe(ConfigType.Boolean);
        configs["max-items"].Type.ShouldBe(ConfigType.Integer);
        configs["discount-rate"].Type.ShouldBe(ConfigType.Float);
        configs["greeting"].Type.ShouldBe(ConfigType.String);
        configs["settings"].Type.ShouldBe(ConfigType.Json);
    }

    [Fact]
    public async Task ServesTheValuesInAFileWithTheConfigTypeEachValueImplies()
    {
        var file = ValuesFile("""
            {
              "new-checkout": true,
              "max-items": 20,
              "discount-rate": 0.15,
              "greeting": "Bye",
              "settings": { "theme": "dark" },
              "tags": ["a", "b"]
            }
            """);
        await using var client = new ConfigDirectorClient(Offline(file: file));

        await client.InitializeAsync(Cancellation);

        client.GetValue("new-checkout", false).ShouldBeTrue();
        client.GetValue("max-items", 0).ShouldBe(20);
        client.GetValue("discount-rate", 0.0).ShouldBe(0.15);
        client.GetValue("greeting", "Hello").ShouldBe("Bye");
        client.GetValue("settings", EmptyJson).GetProperty("theme").GetString().ShouldBe("dark");
        client.GetValue("tags", EmptyJson).EnumerateArray().Select(tag => tag.GetString()).ShouldBe(["a", "b"]);
        var configs = client.GetAllConfigs();
        configs["new-checkout"].Type.ShouldBe(ConfigType.Boolean);
        configs["max-items"].Type.ShouldBe(ConfigType.Integer);
        configs["discount-rate"].Type.ShouldBe(ConfigType.Float);
        configs["greeting"].Type.ShouldBe(ConfigType.String);
        configs["settings"].Type.ShouldBe(ConfigType.Json);
        configs["tags"].Type.ShouldBe(ConfigType.Json);
    }

    [Fact]
    public async Task ReadsAWholeNumberWrittenWithADecimalPointInAFileAsAnInteger()
    {
        var file = ValuesFile("""{ "max-items": 20.0 }""");
        await using var client = new ConfigDirectorClient(Offline(file: file));

        await client.InitializeAsync(Cancellation);

        var maxItems = client.GetAllConfigs()["max-items"];
        maxItems.Type.ShouldBe(ConfigType.Integer);
        maxItems.Value.ShouldBe("20");
    }

    [Fact]
    public async Task ReadsAWholeNumberWrittenWithAnExponentInAFileAsAnInteger()
    {
        var file = ValuesFile("""{ "page-size": 5e1 }""");
        await using var client = new ConfigDirectorClient(Offline(file: file));

        await client.InitializeAsync(Cancellation);

        var pageSize = client.GetAllConfigs()["page-size"];
        pageSize.Type.ShouldBe(ConfigType.Integer);
        pageSize.Value.ShouldBe("50");
    }

    [Fact]
    public async Task ReadsAWholeNumberInAFileTooLargeForALongAsAFloat()
    {
        var file = ValuesFile("""{ "visitors": 1e19 }""");
        await using var client = new ConfigDirectorClient(Offline(file: file));

        await client.InitializeAsync(Cancellation);

        var visitors = client.GetAllConfigs()["visitors"];
        visitors.Type.ShouldBe(ConfigType.Float);
        visitors.Value.ShouldBe("10000000000000000000");
    }

    [Fact]
    public async Task ServesTheSameValueToEveryContextAsAMatch()
    {
        await using var client = new ConfigDirectorClient(Offline(values: new Dictionary<string, object>
        {
            ["new-checkout"] = true,
        }));
        var evaluations = Evaluations(client);
        await client.InitializeAsync(Cancellation);

        client.GetValue("new-checkout", false, new Context { Id = "user-1", Traits = { ["plan"] = "pro" } }).ShouldBeTrue();
        client.GetValue("new-checkout", false, new Context { Id = "org-1", Kind = "organization" }).ShouldBeTrue();
        client.GetValue("new-checkout", false).ShouldBeTrue();

        evaluations.Select(evaluation => evaluation.Reason).ShouldBe(
            [EvaluationReason.FoundMatch, EvaluationReason.FoundMatch, EvaluationReason.FoundMatch]);
        evaluations.Select(evaluation => evaluation.ValueId).ShouldBe([null, null, null]);
    }

    [Fact]
    public async Task ServesEveryInCodeDefaultValueWhenGivenNoValues()
    {
        await using var client = new ConfigDirectorClient(Offline());
        var evaluations = Evaluations(client);

        await client.InitializeAsync(Cancellation);

        client.IsReady.ShouldBeTrue();
        client.GetValue("new-checkout", true).ShouldBeTrue();
        evaluations.Single().Reason.ShouldBe(EvaluationReason.ConfigStateMissing);
    }

    [Fact]
    public async Task ServesTheInCodeDefaultValueForAValueReadAsAnotherType()
    {
        await using var client = new ConfigDirectorClient(Offline(values: new Dictionary<string, object>
        {
            ["greeting"] = "Bye",
        }));
        var evaluations = Evaluations(client);
        await client.InitializeAsync(Cancellation);

        client.GetValue("greeting", 7).ShouldBe(7);

        evaluations.Single().IsDefault.ShouldBeTrue();
        evaluations.Single().Reason.ShouldBe(EvaluationReason.InvalidNumber);
    }

    [Fact]
    public async Task SendsNothingToConfigDirector()
    {
        var client = new ConfigDirectorClient(Offline(values: new Dictionary<string, object>
        {
            ["new-checkout"] = true,
        }));
        await client.InitializeAsync(Cancellation);
        client.GetValue("new-checkout", false, new Context { Id = "user-1" });
        client.GetValue("missing-config", "fallback");

        await client.DisposeAsync();

        _server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task SendsNothingToConfigDirectorWhenServingAFile()
    {
        var client = new ConfigDirectorClient(Offline(file: ValuesFile("""{ "new-checkout": true }""")));
        await client.InitializeAsync(Cancellation);
        client.GetValue("new-checkout", false, new Context { Id = "user-1" });
        client.GetValue("missing-config", "fallback");

        await client.DisposeAsync();

        _server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task ServesEveryInCodeDefaultValueWhenTheFileIsEmpty()
    {
        await using var client = new ConfigDirectorClient(Offline(file: string.Empty));
        var evaluations = Evaluations(client);

        await client.InitializeAsync(Cancellation);

        client.IsReady.ShouldBeTrue();
        client.GetValue("new-checkout", true).ShouldBeTrue();
        evaluations.Single().Reason.ShouldBe(EvaluationReason.ConfigStateMissing);
    }

    [Fact]
    public async Task ConnectsAsUsualWhenTheFileIsBlankWhileStreaming()
    {
        var options = Offline(file: "   ");
        options.Connection.Mode = ConnectionMode.Streaming;
        options.Connection.Timeout = SdkServer.PatientTimeout;
        await using var client = new ConfigDirectorClient("server-sdk-key", options);

        await client.InitializeAsync(Cancellation);

        client.IsReady.ShouldBeTrue();
        _server.Requests.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task IgnoresThePollingIntervalTheTimeoutAndTheUrl()
    {
        using var loggerFactory = new CapturingLoggerFactory();
        var options = Offline(values: new Dictionary<string, object> { ["new-checkout"] = true });
        options.LoggerFactory = loggerFactory;
        options.Connection.PollingInterval = TimeSpan.FromSeconds(10);
        options.Connection.Timeout = TimeSpan.FromTicks(1);
        options.Connection.Url = SdkServer.UnreachableUrl;
        await using var client = new ConfigDirectorClient(options);

        await client.InitializeAsync(Cancellation);

        client.IsReady.ShouldBeTrue();
        client.GetValue("new-checkout", false).ShouldBeTrue();
        loggerFactory.Logger.Entries.Where(entry => entry.Level >= LogLevel.Warning).ShouldBeEmpty();
    }

    [Fact]
    public async Task IsReadyOnceTheValuesAreLoadedAndRaisesClientReadyOnce()
    {
        await using var client = new ConfigDirectorClient(Offline(values: new Dictionary<string, object>
        {
            ["new-checkout"] = true,
        }));
        var readyEvents = 0;
        client.ClientReady += (_, _) => readyEvents++;
        client.IsReady.ShouldBeFalse();

        await client.InitializeAsync(Cancellation);
        await client.InitializeAsync(Cancellation);

        client.IsReady.ShouldBeTrue();
        readyEvents.ShouldBe(1);
    }

    [Fact]
    public async Task CallsAWatchOnceWithTheOfflineValue()
    {
        await using var client = new ConfigDirectorClient(Offline(values: new Dictionary<string, object>
        {
            ["new-checkout"] = true,
        }));
        var seen = new List<bool>();
        using var watch = client.Watch("new-checkout", false, seen.Add);

        await client.InitializeAsync(Cancellation);
        client.GetValue("new-checkout", false);

        seen.ShouldBe([true]);
    }

    [Fact]
    public async Task ReadsTheFileAgainWhenInitializedAgain()
    {
        var file = ValuesFile("""{ "max-items": 20 }""");
        await using var client = new ConfigDirectorClient(Offline(file: file));
        var seen = new List<int>();
        using var watch = client.Watch("max-items", 0, seen.Add);
        await client.InitializeAsync(Cancellation);

        File.WriteAllText(file, """{ "max-items": 50 }""");
        await client.InitializeAsync(Cancellation);

        client.GetValue("max-items", 0).ShouldBe(50);
        seen.ShouldBe([20, 50]);
    }

    [Fact]
    public async Task KeepsTheValuesItLoadedWhenALaterInitializationFails()
    {
        var file = ValuesFile("""{ "max-items": 20 }""");
        await using var client = new ConfigDirectorClient(Offline(file: file));
        await client.InitializeAsync(Cancellation);

        File.WriteAllText(file, """{ "max-items": null }""");
        await Should.ThrowAsync<ConfigDirectorInitializationException>(() => client.InitializeAsync(Cancellation));

        client.IsReady.ShouldBeTrue();
        client.GetValue("max-items", 0).ShouldBe(20);
    }

    [Fact]
    public async Task RejectsInitializationWhenTheFileDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"configdirector-missing-{Guid.NewGuid():N}.json");
        await using var client = new ConfigDirectorClient(Offline(file: missing));

        var failure = await Should.ThrowAsync<ConfigDirectorInitializationException>(
            () => client.InitializeAsync(Cancellation));

        failure.Message.ShouldContain(missing);
        client.IsReady.ShouldBeFalse();
    }

    [Fact]
    public async Task RejectsInitializationWhenTheFileCannotBeRead()
    {
        var directory = Path.GetTempPath();
        await using var client = new ConfigDirectorClient(Offline(file: directory));

        var failure = await Should.ThrowAsync<ConfigDirectorInitializationException>(
            () => client.InitializeAsync(Cancellation));

        failure.Message.ShouldContain(directory);
        client.IsReady.ShouldBeFalse();
    }

    [Fact]
    public async Task RejectsInitializationWhenTheFileIsNotJson()
    {
        var file = ValuesFile("new-checkout: true");
        await using var client = new ConfigDirectorClient(Offline(file: file));

        var failure = await Should.ThrowAsync<ConfigDirectorInitializationException>(
            () => client.InitializeAsync(Cancellation));

        failure.Message.ShouldContain(file);
        client.IsReady.ShouldBeFalse();
    }

    [Fact]
    public async Task RejectsInitializationWhenTheFileDoesNotHoldAnObject()
    {
        var file = ValuesFile("""[{ "new-checkout": true }]""");
        await using var client = new ConfigDirectorClient(Offline(file: file));

        var failure = await Should.ThrowAsync<ConfigDirectorInitializationException>(
            () => client.InitializeAsync(Cancellation));

        failure.Message.ShouldContain(file);
        client.IsReady.ShouldBeFalse();
    }

    [Fact]
    public async Task RejectsInitializationWhenTheFileHoldsANull()
    {
        var file = ValuesFile("""{ "new-checkout": true, "max-items": null }""");
        await using var client = new ConfigDirectorClient(Offline(file: file));

        var failure = await Should.ThrowAsync<ConfigDirectorInitializationException>(
            () => client.InitializeAsync(Cancellation));

        failure.Message.ShouldContain(file);
        failure.Message.ShouldContain("max-items");
        client.IsReady.ShouldBeFalse();
    }

    [Fact]
    public async Task RejectsInitializationWhenTheFileHoldsANumberTooLargeToRead()
    {
        var file = ValuesFile("""{ "visitors": 1e400 }""");
        await using var client = new ConfigDirectorClient(Offline(file: file));

        var failure = await Should.ThrowAsync<ConfigDirectorInitializationException>(
            () => client.InitializeAsync(Cancellation));

        failure.Message.ShouldContain("visitors");
        failure.Message.ShouldContain("1e400");
        client.IsReady.ShouldBeFalse();
    }

    [Fact]
    public async Task RejectsInitializationWhenAnInlineValueHasAnUnsupportedType()
    {
        await using var client = new ConfigDirectorClient(Offline(values: new Dictionary<string, object>
        {
            ["launch-date"] = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc),
        }));

        var failure = await Should.ThrowAsync<ConfigDirectorInitializationException>(
            () => client.InitializeAsync(Cancellation));

        failure.Message.ShouldContain("launch-date");
        client.IsReady.ShouldBeFalse();
    }

    [Fact]
    public async Task RejectsInitializationWhenGivenAFileAndValuesTogether()
    {
        var file = ValuesFile("""{ "new-checkout": true }""");
        await using var client = new ConfigDirectorClient(Offline(
            file: file, values: new Dictionary<string, object> { ["new-checkout"] = false }));

        await Should.ThrowAsync<ConfigDirectorInitializationException>(() => client.InitializeAsync(Cancellation));

        client.IsReady.ShouldBeFalse();
    }

    [Fact]
    public async Task RejectsInitializationWithoutConnectingWhenGivenAFileWhileStreaming()
    {
        var options = Offline(file: ValuesFile("""{ "new-checkout": true }"""));
        options.Connection.Mode = ConnectionMode.Streaming;
        await using var client = new ConfigDirectorClient("server-sdk-key", options);

        await Should.ThrowAsync<ConfigDirectorInitializationException>(() => client.InitializeAsync(Cancellation));

        client.IsReady.ShouldBeFalse();
        _server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task RejectsInitializationWithoutConnectingWhenGivenValuesWhilePolling()
    {
        var options = Offline(values: new Dictionary<string, object> { ["new-checkout"] = true });
        options.Connection.Mode = ConnectionMode.Polling;
        await using var client = new ConfigDirectorClient("server-sdk-key", options);

        await Should.ThrowAsync<ConfigDirectorInitializationException>(() => client.InitializeAsync(Cancellation));

        client.IsReady.ShouldBeFalse();
        _server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task RejectsInitializationWhenAnInlineValueContainsItself()
    {
        var loop = new List<object>();
        loop.Add(loop);
        await using var client = new ConfigDirectorClient(Offline(values: new Dictionary<string, object>
        {
            ["loop"] = loop,
        }));

        var failure = await Should.ThrowAsync<ConfigDirectorInitializationException>(
            () => client.InitializeAsync(Cancellation));

        failure.Message.ShouldContain("loop");
        client.IsReady.ShouldBeFalse();
    }

    public void Dispose()
    {
        _server.Dispose();
        foreach (var file in _files)
        {
            File.Delete(file);
        }
    }

    private string ValuesFile(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"configdirector-offline-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        _files.Add(path);

        return path;
    }

    private static List<ConfigEvaluation> Evaluations(ConfigDirectorClient client)
    {
        var evaluations = new List<ConfigEvaluation>();
        client.ConfigEvaluated += (_, args) => evaluations.Add(args.Evaluation);

        return evaluations;
    }

    private ConfigDirectorClientOptions Offline(
        string? file = null, IReadOnlyDictionary<string, object>? values = null) =>
        new()
        {
            Connection =
            {
                Mode = ConnectionMode.Offline,
                File = file,
                Values = values,
                Url = _server.BaseUrl,
            },
        };
}
