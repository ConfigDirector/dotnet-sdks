using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigDirector.Tests;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Testing.Tests;

public sealed class TestClientConformanceTests : IAsyncDisposable
{
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongerThanTheTestWaits = TimeSpan.FromSeconds(30);

    private readonly CapturingLoggerFactory _loggerFactory = new();
    private readonly List<TestClient> _testClients = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var testClient in _testClients)
        {
            await testClient.DisposeAsync();
        }

        _loggerFactory.Dispose();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private TestClient Create(IReadOnlyDictionary<string, object>? values = null, TimeSpan? timeout = null)
    {
        var testClient = ConfigDirectorTesting.CreateTestClient(
            values, new TestClientOptions { Timeout = timeout, LoggerFactory = _loggerFactory });
        _testClients.Add(testClient);
        return testClient;
    }

    private static Dictionary<string, object> Values(params (string Key, object Value)[] entries)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            values[key] = value;
        }

        return values;
    }

    private List<string> Errors() =>
        _loggerFactory.Logger.Entries
            .Where(entry => entry.Level == LogLevel.Error)
            .Select(entry => entry.Message)
            .ToList();

    private List<string> Warnings() =>
        _loggerFactory.Logger.Entries
            .Where(entry => entry.Level == LogLevel.Warning)
            .Select(entry => entry.Message)
            .ToList();

    private static List<ConfigsUpdatedEventArgs> UpdatesOf(IConfigDirectorClient client)
    {
        var updates = new List<ConfigsUpdatedEventArgs>();
        client.ConfigsUpdated += (_, updated) => updates.Add(updated);
        return updates;
    }

    private static List<ConfigEvaluation> EvaluationsOf(IConfigDirectorClient client)
    {
        var evaluations = new List<ConfigEvaluation>();
        client.ConfigEvaluated += (_, evaluated) => evaluations.Add(evaluated.Evaluation);
        return evaluations;
    }

    private static Func<int> ReadyCountOf(IConfigDirectorClient client)
    {
        var count = 0;
        client.ClientReady += (_, _) => count++;
        return () => count;
    }

    [Fact]
    public async Task S1ReadsEverySeededValueTypeAfterInitialize()
    {
        var client = Create(Values(
            ("flag", true),
            ("count", 20),
            ("ratio", 2.5),
            ("greeting", "hello"),
            ("theme", Values(("color", "blue"))),
            ("tags", new List<object> { "a", "b" }))).Client;

        await client.InitializeAsync(Cancellation);

        client.IsReady.ShouldBeTrue();
        client.GetValue("flag", false).ShouldBeTrue();
        client.GetValue("count", 0).ShouldBe(20);
        client.GetValue("ratio", 0.0).ShouldBe(2.5);
        client.GetValue("greeting", "x").ShouldBe("hello");
        client.GetValue("theme", default(JsonElement)).GetProperty("color").GetString().ShouldBe("blue");
        client.GetValue("tags", default(JsonElement)).GetArrayLength().ShouldBe(2);
        client.GetAllConfigs().Keys.OrderBy(key => key, StringComparer.Ordinal)
            .ShouldBe(["count", "flag", "greeting", "ratio", "tags", "theme"]);
    }

    [Fact]
    public async Task ServesTheSameValueToEveryContext()
    {
        var client = Create(Values(("flag", true))).Client;
        await client.InitializeAsync(Cancellation);

        client.GetValue("flag", false, new Context { Id = "user-a" }).ShouldBeTrue();
        client.GetValue("flag", false, new Context { Id = "user-b", Traits = { ["plan"] = "pro" } }).ShouldBeTrue();
    }

    [Fact]
    public async Task S2ReadingABooleanAsAStringReturnsTheInCodeDefaultWithTypeMismatch()
    {
        var client = Create(Values(("flag", true))).Client;
        var evaluations = EvaluationsOf(client);
        await client.InitializeAsync(Cancellation);

        client.GetValue("flag", "fallback").ShouldBe("fallback");

        evaluations[evaluations.Count - 1].Reason.ShouldBe(EvaluationReason.TypeMismatch);
    }

    [Fact]
    public async Task S3SetValueOnAConnectedClientChangesTheNextRead()
    {
        var testClient = Create(Values(("count", 20)));
        await testClient.Client.InitializeAsync(Cancellation);

        testClient.SetValue("count", 25);

        testClient.Client.GetValue("count", 0).ShouldBe(25);
    }

    [Fact]
    public async Task S4SetValueFiresTheKeysWatchAndListsTheKeyInConfigsUpdated()
    {
        var testClient = Create(Values(("count", 20)));
        var client = testClient.Client;
        var watched = new List<int>();
        client.Watch("count", 0, watched.Add);
        var updates = UpdatesOf(client);
        await client.InitializeAsync(Cancellation);

        testClient.SetValue("count", 25);

        watched.ShouldBe([20, 25]);
        updates[updates.Count - 1].Keys.ShouldBe(["count"]);
        updates[updates.Count - 1].RemovedKeys.ShouldBe([]);
    }

    [Fact]
    public async Task S5SetValueOfAnotherKeyDoesNotFireAnUnrelatedWatch()
    {
        var testClient = Create(Values(("a", 1), ("b", 1)));
        var watchedA = new List<int>();
        testClient.Client.Watch("a", 0, watchedA.Add);
        await testClient.Client.InitializeAsync(Cancellation);

        testClient.SetValue("b", 2);

        watchedA.ShouldBe([1]);
    }

    [Fact]
    public async Task S6RemoveValueMakesReadsReturnTheInCodeDefaultWithConfigStateMissing()
    {
        var testClient = Create(Values(("flag", true)));
        var evaluations = EvaluationsOf(testClient.Client);
        await testClient.Client.InitializeAsync(Cancellation);

        testClient.RemoveValue("flag");

        testClient.Client.GetValue("flag", false).ShouldBeFalse();
        evaluations[evaluations.Count - 1].Reason.ShouldBe(EvaluationReason.ConfigStateMissing);
    }

    [Fact]
    public async Task S7RemoveValueFiresTheWatchWithTheDefaultAndListsTheKeyInRemovedKeys()
    {
        var testClient = Create(Values(("flag", true), ("count", 20)));
        var client = testClient.Client;
        var watched = new List<bool>();
        client.Watch("flag", false, watched.Add);
        var updates = UpdatesOf(client);
        await client.InitializeAsync(Cancellation);

        testClient.RemoveValue("flag");

        watched.ShouldBe([true, false]);
        updates[updates.Count - 1].Keys.ShouldBe(["count"]);
        updates[updates.Count - 1].RemovedKeys.ShouldBe(["flag"]);
    }

    [Fact]
    public async Task S8AValueSetBeforeInitializeIsDeliveredByTheFirstAttempt()
    {
        var testClient = Create(Values(("flag", true)));
        var updates = UpdatesOf(testClient.Client);

        testClient.SetValue("greeting", "hello");
        await testClient.Client.InitializeAsync(Cancellation);

        updates.Count.ShouldBe(1);
        updates[0].Keys.ShouldBe(["flag", "greeting"]);
        testClient.Client.GetValue("greeting", "x").ShouldBe("hello");
    }

    [Fact]
    public async Task S9AHeldInitializeCompletesReadyWhenCompleted()
    {
        var testClient = Create(Values(("flag", true)), LongerThanTheTestWaits);
        var client = testClient.Client;
        var ready = ReadyCountOf(client);
        testClient.HoldInitialization();

        var initializing = client.InitializeAsync(Cancellation);
        client.IsReady.ShouldBeFalse();
        ready().ShouldBe(0);

        testClient.CompleteInitialization();

        client.IsReady.ShouldBeTrue();
        ready().ShouldBe(1);
        client.GetValue("flag", false).ShouldBeTrue();
        await initializing.WaitAsync(Promptly, Cancellation);
    }

    [Fact]
    public async Task S10AValueSetWhileHeldIsDeliveredOnCompletion()
    {
        var testClient = Create(Values(("flag", true)), LongerThanTheTestWaits);
        var client = testClient.Client;
        testClient.HoldInitialization();
        var initializing = client.InitializeAsync(Cancellation);

        testClient.SetValue("flag", false);
        client.IsReady.ShouldBeFalse();
        testClient.CompleteInitialization();

        client.GetValue("flag", true).ShouldBeFalse();
        await initializing.WaitAsync(Promptly, Cancellation);
    }

    [Fact]
    public async Task S11AHeldInitializeThatTimesOutCompletesNotReadyAndTheNextOneIsReady()
    {
        var testClient = Create(Values(("flag", true)), TimeSpan.FromMilliseconds(200));
        var client = testClient.Client;
        var ready = ReadyCountOf(client);
        testClient.HoldInitialization();

        await client.InitializeAsync(Cancellation);

        client.IsReady.ShouldBeFalse();
        Warnings().ShouldContain(warning => warning.StartsWith("Timed out waiting for initialization", StringComparison.Ordinal));
        testClient.CompleteInitialization();
        client.IsReady.ShouldBeFalse();
        ready().ShouldBe(0);

        await client.InitializeAsync(Cancellation);

        client.IsReady.ShouldBeTrue();
        ready().ShouldBe(1);
        client.GetValue("flag", false).ShouldBeTrue();
    }

    [Fact]
    public async Task S12AFailedInitializeCompletesPromptlyNotReadyWithoutClientReadyAndLogsTheError()
    {
        var testClient = Create(Values(("flag", true)), LongerThanTheTestWaits);
        var client = testClient.Client;
        var ready = ReadyCountOf(client);
        testClient.FailInitialization();

        var stopwatch = Stopwatch.StartNew();
        await client.InitializeAsync(Cancellation);

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        client.IsReady.ShouldBeFalse();
        ready().ShouldBe(0);
        Errors().ShouldContain(error => error.Contains("Connection failed with status: 401", StringComparison.Ordinal));
        Errors().ShouldContain(error => error.StartsWith("Initialization failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task S14AfterDisposeTheControlsAreSilentNoOps()
    {
        var testClient = Create(Values(("flag", true)));
        var client = testClient.Client;
        var watched = new List<bool>();
        client.Watch("flag", false, watched.Add);
        await client.InitializeAsync(Cancellation);

        await testClient.DisposeAsync();

        client.IsClosed.ShouldBeTrue();
        Should.NotThrow(() =>
        {
            testClient.SetValue("flag", false);
            testClient.RemoveValue("flag");
            testClient.ReplaceValues(Values(("flag", false)));
            testClient.HoldInitialization();
            testClient.CompleteInitialization();
            testClient.FailInitialization();
        });
        watched.ShouldBe([true]);
    }

    [Fact]
    public async Task S15TwoTestClientsInOneTestNeverShareValues()
    {
        var first = Create(Values(("flag", true)));
        var second = Create(Values(("flag", false)));
        await first.Client.InitializeAsync(Cancellation);
        await second.Client.InitializeAsync(Cancellation);

        second.SetValue("flag", true);
        second.SetValue("count", 1);

        first.Client.GetValue("flag", false).ShouldBeTrue();
        first.Client.GetValue("count", 0).ShouldBe(0);
        second.Client.GetValue("count", 0).ShouldBe(1);
    }

    [Fact]
    public async Task S17AfterDisposeTheInMemoryConnectionHoldsNoAttempt()
    {
        var testClient = Create(Values(("flag", true)));
        await testClient.Client.InitializeAsync(Cancellation);
        testClient.SetValue("flag", false);

        await testClient.DisposeAsync();

        testClient.IsHoldingAnAttempt.ShouldBeFalse();
        testClient.Client.IsClosed.ShouldBeTrue();
    }

    [Fact]
    public async Task S23AFullScenarioMakesNoHttpRequest()
    {
        using var requests = new HttpRequestObserver();
        var testClient = Create(Values(("flag", true), ("count", 20), ("greeting", "hello")));
        var client = testClient.Client;

        await client.InitializeAsync(Cancellation);
        client.GetValue("flag", false).ShouldBeTrue();
        client.GetValue("count", 0).ShouldBe(20);
        client.GetValue("greeting", "x").ShouldBe("hello");
        testClient.SetValue("flag", false);
        client.GetValue("flag", true).ShouldBeFalse();
        await testClient.DisposeAsync();

        requests.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task S24AValueRemovedBeforeInitializeReadsAsTheInCodeDefaultWithConfigStateMissing()
    {
        var testClient = Create(Values(("flag", true), ("count", 20)));
        var evaluations = EvaluationsOf(testClient.Client);

        testClient.RemoveValue("flag");
        await testClient.Client.InitializeAsync(Cancellation);

        testClient.Client.IsReady.ShouldBeTrue();
        testClient.Client.GetValue("flag", false).ShouldBeFalse();
        evaluations[evaluations.Count - 1].Reason.ShouldBe(EvaluationReason.ConfigStateMissing);
    }

    [Fact]
    public async Task S25TheAttemptAfterAFailureSucceedsWithTheValuesStoredInTheMeantime()
    {
        var testClient = Create(Values(("flag", true)));
        var client = testClient.Client;
        testClient.FailInitialization();
        await client.InitializeAsync(Cancellation);
        client.IsReady.ShouldBeFalse();

        testClient.SetValue("count", 4);
        await client.InitializeAsync(Cancellation);

        client.IsReady.ShouldBeTrue();
        client.GetValue("count", 0).ShouldBe(4);
        client.GetValue("flag", false).ShouldBeTrue();
    }

    [Fact]
    public async Task S32ReplaceValuesServesExactlyTheNewValuesAndFiresTheWatchOfADroppedKey()
    {
        var testClient = Create(Values(("a", 1), ("b", 2)));
        var client = testClient.Client;
        var watchedB = new List<int>();
        client.Watch("b", 0, watchedB.Add);
        var evaluations = EvaluationsOf(client);
        await client.InitializeAsync(Cancellation);

        testClient.ReplaceValues(Values(("a", 10), ("c", 3)));

        client.GetValue("a", 0).ShouldBe(10);
        client.GetValue("c", 0).ShouldBe(3);
        client.GetValue("b", 0).ShouldBe(0);
        evaluations[evaluations.Count - 1].Reason.ShouldBe(EvaluationReason.ConfigStateMissing);
        watchedB.ShouldBe([2, 0]);
    }

    [Fact]
    public async Task S33DisposingTheClientEndsAHeldInitializePromptlyNotReady()
    {
        var testClient = Create(Values(("flag", true)), LongerThanTheTestWaits);
        var client = testClient.Client;
        testClient.HoldInitialization();
        var initializing = client.InitializeAsync(Cancellation);
        testClient.IsHoldingAnAttempt.ShouldBeTrue();

        await testClient.DisposeAsync();

        await initializing.WaitAsync(Promptly, Cancellation);
        client.IsReady.ShouldBeFalse();
        testClient.IsHoldingAnAttempt.ShouldBeFalse();
    }

    [Fact]
    public async Task S38CompletingBeforeInitializePicksTheHoldUpLetsItProceedAtOnce()
    {
        var testClient = Create(Values(("flag", true)), LongerThanTheTestWaits);
        testClient.HoldInitialization();
        testClient.CompleteInitialization();

        var stopwatch = Stopwatch.StartNew();
        await testClient.Client.InitializeAsync(Cancellation);

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        testClient.Client.IsReady.ShouldBeTrue();
        testClient.Client.GetValue("flag", false).ShouldBeTrue();
    }

    [Fact]
    public async Task S40ReplaceValuesDisarmsAnArmedHold()
    {
        var testClient = Create(Values(("flag", true)), LongerThanTheTestWaits);
        testClient.HoldInitialization();

        testClient.ReplaceValues(Values(("count", 5)));
        var stopwatch = Stopwatch.StartNew();
        await testClient.Client.InitializeAsync(Cancellation);

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        testClient.Client.IsReady.ShouldBeTrue();
        testClient.Client.GetValue("count", 0).ShouldBe(5);
        testClient.Client.GetValue("flag", false).ShouldBeFalse();
    }

    [Fact]
    public async Task S41ASetValueFromAWatchIsDeliveredAfterTheOuterDelivery()
    {
        var testClient = Create(Values(("a", 1), ("b", 1)));
        var client = testClient.Client;
        var ready = ReadyCountOf(client);
        var order = new List<string>();
        client.Watch("a", 0, value =>
        {
            if (value == 2)
            {
                testClient.SetValue("b", 2);
                order.Add("a-watch-returned");
            }
        });
        client.Watch("b", 0, value => order.Add("b=" + value));
        await client.InitializeAsync(Cancellation);
        order.Clear();

        testClient.SetValue("a", 2);

        order.ShouldBe(["a-watch-returned", "b=2"]);
        ready().ShouldBe(1);
        client.GetValue("a", 0).ShouldBe(2);
        client.GetValue("b", 0).ShouldBe(2);
    }

    [Fact]
    public async Task AStringIsAlwaysAStringConfigAndAJsonNodeIsAJsonConfig()
    {
        var testClient = Create(Values(("text", "{\"a\":1}"), ("document", JsonNode.Parse("{\"a\":1}")!)));
        await testClient.Client.InitializeAsync(Cancellation);

        var configs = testClient.Client.GetAllConfigs();
        configs["text"].Type.ShouldBe(ConfigType.String);
        configs["document"].Type.ShouldBe(ConfigType.Json);
        testClient.Client.GetValue("text", "x").ShouldBe("{\"a\":1}");
        testClient.Client.GetValue("document", default(JsonElement)).GetProperty("a").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task RejectedValuesThrowAndChangeNothing()
    {
        var testClient = Create(Values(("flag", true)));
        await testClient.Client.InitializeAsync(Cancellation);

        Should.Throw<ArgumentNullException>(() => testClient.SetValue("flag", null!));
        Should.Throw<ArgumentException>(() => testClient.SetValue("flag", new object()));
        Should.Throw<ArgumentException>(() => testClient.SetValue("flag", new BigInteger(1)));
        Should.Throw<ArgumentException>(() => testClient.SetValue("flag", double.NaN));
        Should.Throw<ArgumentException>(() => testClient.SetValue(" ", true));
        Should.Throw<ArgumentException>(() => testClient.SetValue("flag", new Dictionary<int, string> { [1] = "one" }));
        Should.Throw<ArgumentNullException>(() => testClient.ReplaceValues(Values(("flag", null!))));
        Should.Throw<ArgumentNullException>(() => testClient.ReplaceValues(null!));
        Should.Throw<ArgumentNullException>(() => ConfigDirectorTesting.CreateTestClient(Values(("flag", null!))));

        testClient.Client.GetValue("flag", false).ShouldBeTrue();
    }

    [Fact]
    public async Task AnEmptyStringServesTheInCodeDefaultWithValueMissing()
    {
        var testClient = Create(Values(("greeting", "")));
        var evaluations = EvaluationsOf(testClient.Client);
        await testClient.Client.InitializeAsync(Cancellation);

        testClient.Client.GetValue("greeting", "fallback").ShouldBe("fallback");

        evaluations[evaluations.Count - 1].Reason.ShouldBe(EvaluationReason.ValueMissing);
    }

    [Fact]
    public async Task TheFirstUpdateFiresConfigsUpdatedBeforeClientReady()
    {
        var testClient = Create(Values(("flag", true)));
        var client = testClient.Client;
        var order = new List<string>();
        var watched = new List<bool>();
        client.Watch("flag", false, watched.Add);
        client.ConfigsUpdated += (_, _) => order.Add("configs-updated");
        client.ClientReady += (_, _) => order.Add("client-ready");

        await client.InitializeAsync(Cancellation);

        order.ShouldBe(["configs-updated", "client-ready"]);
        watched.ShouldBe([true]);
    }

    [Fact]
    public async Task AnExceptionThrownByAWatchIsCaughtAndLogged()
    {
        var testClient = Create(Values(("flag", true)));
        var client = testClient.Client;
        await client.InitializeAsync(Cancellation);
        client.Watch("flag", false, _ => throw new InvalidOperationException("from the watch"));

        Should.NotThrow(() => testClient.SetValue("flag", false));

        client.GetValue("flag", true).ShouldBeFalse();
        var logged = _loggerFactory.Logger.Entries.Single(entry => entry.Message == "A watch on flag threw.");
        logged.Level.ShouldBe(LogLevel.Error);
        logged.Error.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("from the watch");
    }

    [Fact]
    public async Task AfterDisposeReadsReturnTheDefaultWithoutEventsAndInitializeThrows()
    {
        var testClient = Create(Values(("flag", true)));
        var client = testClient.Client;
        var evaluations = EvaluationsOf(client);
        await client.InitializeAsync(Cancellation);

        await testClient.DisposeAsync();

        client.IsReady.ShouldBeFalse();
        client.GetValue("flag", false).ShouldBeFalse();
        evaluations.ShouldBeEmpty();
        client.GetAllConfigs().ShouldBeEmpty();
        await Should.ThrowAsync<ObjectDisposedException>(() => client.InitializeAsync(Cancellation));
    }

    private sealed class HttpRequestObserver : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> _subscriptions = [];

        internal HttpRequestObserver() => _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));

        internal List<string> Events { get; } = [];

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "HttpHandlerDiagnosticListener")
            {
                lock (_subscriptions)
                {
                    _subscriptions.Add(listener.Subscribe(this));
                }
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            lock (Events)
            {
                Events.Add(value.Key);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void Dispose()
        {
            lock (_subscriptions)
            {
                foreach (var subscription in _subscriptions)
                {
                    subscription.Dispose();
                }
            }
        }
    }
}
