using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigDirector.Transport;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Tests.Transport;

public abstract class InMemoryConnectionTests : IAsyncDisposable
{
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongerThanTheTestWaits = TimeSpan.FromSeconds(30);

    private readonly CapturingLoggerFactory _loggerFactory = new();
    private readonly List<InMemoryConnection> _connections = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.Client.DisposeAsync();
        }

        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private InMemoryConnection Connection(IReadOnlyDictionary<string, object> values, TimeSpan? timeout = null)
    {
        var connection = new InMemoryConnection(values, timeout, _loggerFactory);
        _connections.Add(connection);
        return connection;
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

    private static ConfigState StateOf(IConfigDirectorClient client, string key) => client.GetAllConfigs()[key];

    public sealed class Encoding : InMemoryConnectionTests
    {
        [Fact]
        public async Task EachNativeTypeBecomesTheMatchingConfigType()
        {
            var theme = Values(("z", 1), ("a", new List<object> { 1, 2.5, "x" }));
            var client = Connection(Values(
                ("flag", true),
                ("count", 20),
                ("big", 3_000_000_000L),
                ("small", (short)7),
                ("unsigned", 9u),
                ("ratio", 2.5),
                ("share", 0.1f),
                ("price", 1.25m),
                ("greeting", "hello"),
                ("theme", theme),
                ("tags", new List<object> { "a", "b" }),
                ("element", JsonDocument.Parse("{\"b\": [1, {\"c\": null}]}").RootElement),
                ("node", JsonNode.Parse("[true, 2]")!))).Client;
            await client.InitializeAsync(Cancellation);

            StateOf(client, "flag").Type.ShouldBe(ConfigType.Boolean);
            StateOf(client, "flag").Value.ShouldBe("true");
            StateOf(client, "count").Type.ShouldBe(ConfigType.Integer);
            StateOf(client, "count").Value.ShouldBe("20");
            StateOf(client, "big").Value.ShouldBe("3000000000");
            StateOf(client, "small").Type.ShouldBe(ConfigType.Integer);
            StateOf(client, "small").Value.ShouldBe("7");
            StateOf(client, "unsigned").Type.ShouldBe(ConfigType.Integer);
            StateOf(client, "unsigned").Value.ShouldBe("9");
            StateOf(client, "ratio").Type.ShouldBe(ConfigType.Float);
            StateOf(client, "ratio").Value.ShouldBe("2.5");
            StateOf(client, "share").Type.ShouldBe(ConfigType.Float);
            StateOf(client, "share").Value.ShouldBe("0.1");
            StateOf(client, "price").Type.ShouldBe(ConfigType.Float);
            StateOf(client, "price").Value.ShouldBe("1.25");
            StateOf(client, "greeting").Type.ShouldBe(ConfigType.String);
            StateOf(client, "greeting").Value.ShouldBe("hello");
            StateOf(client, "theme").Type.ShouldBe(ConfigType.Json);
            StateOf(client, "theme").Value.ShouldBe("{\"a\":[1,2.5,\"x\"],\"z\":1}");
            StateOf(client, "tags").Type.ShouldBe(ConfigType.Json);
            StateOf(client, "tags").Value.ShouldBe("[\"a\",\"b\"]");
            StateOf(client, "element").Type.ShouldBe(ConfigType.Json);
            StateOf(client, "element").Value.ShouldBe("{\"b\":[1,{\"c\":null}]}");
            StateOf(client, "node").Type.ShouldBe(ConfigType.Json);
            StateOf(client, "node").Value.ShouldBe("[true,2]");

            client.GetValue("flag", false).ShouldBeTrue();
            client.GetValue("count", 0).ShouldBe(20);
            client.GetValue("big", 0L).ShouldBe(3_000_000_000L);
            client.GetValue("ratio", 0.0).ShouldBe(2.5);
            client.GetValue("share", 0.0f).ShouldBe(0.1f);
            client.GetValue("price", 0m).ShouldBe(1.25m);
            client.GetValue("greeting", "x").ShouldBe("hello");
            client.GetValue("theme", default(JsonElement)).GetProperty("z").GetInt32().ShouldBe(1);
            client.GetValue("tags", default(JsonElement)).GetArrayLength().ShouldBe(2);
        }

        [Fact]
        public async Task FloatsAreWrittenInPlainNotationWithoutAnExponent()
        {
            var client = Connection(Values(
                ("tiny", 1e-7),
                ("huge", 1e21),
                ("tiny-float", 1e-7f),
                ("whole", 2.0),
                ("tiny-decimal", 0.0000001m))).Client;
            await client.InitializeAsync(Cancellation);

            StateOf(client, "tiny").Value.ShouldBe("0.0000001");
            StateOf(client, "huge").Value.ShouldBe("1000000000000000000000");
            StateOf(client, "tiny-float").Value.ShouldBe("0.0000001");
            StateOf(client, "whole").Value.ShouldBe("2");
            StateOf(client, "tiny-decimal").Value.ShouldBe("0.0000001");
            client.GetValue("tiny", 0.0).ShouldBe(1e-7);
            client.GetValue("huge", 0.0).ShouldBe(1e21);
            client.GetValue("tiny-float", 0.0f).ShouldBe(1e-7f);
        }

        [Fact]
        public async Task DictionariesAreEncodedWithSortedKeys()
        {
            var reversed = new SortedDictionary<string, object>(Comparer<string>.Create((left, right) =>
                string.CompareOrdinal(right, left)))
            {
                ["a"] = 1,
                ["b"] = 2,
            };
            var client = Connection(Values(
                ("plain", Values(("b", 1), ("a", 2))),
                ("reversed", reversed),
                ("nested", new List<object> { Values(("b", 1), ("a", 2)) }))).Client;
            await client.InitializeAsync(Cancellation);

            StateOf(client, "plain").Value.ShouldBe("{\"a\":2,\"b\":1}");
            StateOf(client, "reversed").Value.ShouldBe("{\"a\":1,\"b\":2}");
            StateOf(client, "nested").Value.ShouldBe("[{\"a\":2,\"b\":1}]");
        }

        [Fact]
        public async Task JsonContentsMayHoldNullBooleansNumbersStringsDictionariesAndLists()
        {
            var contents = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["none"] = null,
                ["flag"] = false,
                ["count"] = 7L,
                ["share"] = 0.5f,
                ["price"] = 1.5m,
                ["text"] = "<a>&\"",
                ["list"] = new List<object> { new List<object> { 1 } },
            };
            var client = Connection(Values(("theme", contents))).Client;
            await client.InitializeAsync(Cancellation);

            StateOf(client, "theme").Value.ShouldBe(
                "{\"count\":7,\"flag\":false,\"list\":[[1]],\"none\":null,\"price\":1.5,\"share\":0.5,\"text\":\"<a>&\\\"\"}");
        }

        [Fact]
        public async Task AStringIsAlwaysAStringConfig()
        {
            var client = Connection(Values(("theme", "{\"a\":1}"))).Client;
            await client.InitializeAsync(Cancellation);

            StateOf(client, "theme").Type.ShouldBe(ConfigType.String);
            client.GetValue("theme", "x").ShouldBe("{\"a\":1}");
        }

        [Fact]
        public async Task UnsupportedValuesAreRejectedAndLeaveTheStoredValuesUnchanged()
        {
            var connection = Connection(Values(("flag", true)));
            await connection.Client.InitializeAsync(Cancellation);

            Should.Throw<ArgumentNullException>(() => connection.SetValue("flag", null!));
            Should.Throw<ArgumentException>(() => connection.SetValue("flag", new object())).Message.ShouldContain("flag");
            Should.Throw<ArgumentException>(() => connection.SetValue("flag", new BigInteger(1)));
            Should.Throw<ArgumentException>(() => connection.SetValue("flag", DateTime.UtcNow));
            Should.Throw<ArgumentException>(() => connection.SetValue("flag", double.NaN));
            Should.Throw<ArgumentException>(() => connection.SetValue("flag", float.PositiveInfinity));
            Should.Throw<ArgumentException>(() => connection.SetValue("flag", Values(("a", new object()))))
                .Message.ShouldContain("a");
            Should.Throw<ArgumentException>(() => connection.SetValue("flag", new List<object> { 1, double.NaN }))
                .Message.ShouldContain("[1]");
            Should.Throw<ArgumentException>(() => connection.SetValue("flag", new Dictionary<int, string> { [1] = "one" }));
            Should.Throw<ArgumentException>(() => connection.SetValue("flag", JsonDocument.Parse("42").RootElement));
            Should.Throw<ArgumentException>(() => connection.SetValue("flag", JsonValue.Create(42)!));
            Should.Throw<ArgumentException>(() => connection.SetValue(" ", true));
            Should.Throw<ArgumentNullException>(() => connection.SetValue(null!, true));
            Should.Throw<ArgumentNullException>(() => connection.ReplaceValues(Values(("flag", null!))));
            Should.Throw<ArgumentNullException>(() => Connection(Values(("flag", null!))));

            connection.Client.GetValue("flag", false).ShouldBeTrue();
        }
    }

    public sealed class DefaultMode : InMemoryConnectionTests
    {
        [Fact]
        public async Task InitializeCompletesReadyWithTheSeededValuesAndRaisesClientReady()
        {
            var client = Connection(Values(("flag", true), ("count", 20))).Client;
            var ready = ReadyCountOf(client);
            var updates = UpdatesOf(client);

            await client.InitializeAsync(Cancellation);

            client.IsReady.ShouldBeTrue();
            ready().ShouldBe(1);
            updates.Count.ShouldBe(1);
            updates[0].Keys.ShouldBe(["count", "flag"]);
            updates[0].RemovedKeys.ShouldBe([]);
            client.GetValue("flag", false).ShouldBeTrue();
            client.GetValue("count", 0).ShouldBe(20);
        }

        [Fact]
        public async Task TheSameValueIsServedToEveryContext()
        {
            var client = Connection(Values(("flag", true))).Client;
            await client.InitializeAsync(Cancellation);

            client.GetValue("flag", false, new Context { Id = "user-a" }).ShouldBeTrue();
            client.GetValue("flag", false, new Context { Id = "user-b", Traits = { ["plan"] = "pro" } }).ShouldBeTrue();
        }

        [Fact]
        public async Task ABooleanReadAsAStringReturnsTheDefaultWithTypeMismatch()
        {
            var client = Connection(Values(("flag", true))).Client;
            var evaluations = EvaluationsOf(client);
            await client.InitializeAsync(Cancellation);

            client.GetValue("flag", "fallback").ShouldBe("fallback");

            evaluations[evaluations.Count - 1].Reason.ShouldBe(EvaluationReason.TypeMismatch);
        }

        [Fact]
        public async Task SetValueOnAConnectedClientDeliversADeltaCarryingOnlyThatKey()
        {
            var connection = Connection(Values(("count", 20), ("other", 1)));
            var client = connection.Client;
            var watchedCount = new List<int>();
            var watchedOther = new List<int>();
            client.Watch("count", 0, watchedCount.Add);
            client.Watch("other", 0, watchedOther.Add);
            var updates = UpdatesOf(client);
            await client.InitializeAsync(Cancellation);

            connection.SetValue("count", 25);

            client.GetValue("count", 0).ShouldBe(25);
            watchedCount.ShouldBe([20, 25]);
            watchedOther.ShouldBe([1]);
            updates[updates.Count - 1].Keys.ShouldBe(["count"]);
            updates[updates.Count - 1].RemovedKeys.ShouldBe([]);
        }

        [Fact]
        public async Task RemoveValueDeliversAFullUpdateWithoutTheKey()
        {
            var connection = Connection(Values(("flag", true), ("count", 20)));
            var client = connection.Client;
            var watched = new List<bool>();
            client.Watch("flag", false, watched.Add);
            var updates = UpdatesOf(client);
            var evaluations = EvaluationsOf(client);
            await client.InitializeAsync(Cancellation);

            connection.RemoveValue("flag");

            client.GetValue("flag", false).ShouldBeFalse();
            evaluations[evaluations.Count - 1].Reason.ShouldBe(EvaluationReason.ConfigStateMissing);
            watched.ShouldBe([true, false]);
            updates[updates.Count - 1].Keys.ShouldBe(["count"]);
            updates[updates.Count - 1].RemovedKeys.ShouldBe(["flag"]);
        }

        [Fact]
        public async Task ReplaceValuesDeliversExactlyTheNewValues()
        {
            var connection = Connection(Values(("a", 1), ("b", 2)));
            var client = connection.Client;
            var watchedB = new List<int>();
            client.Watch("b", 0, watchedB.Add);
            var updates = UpdatesOf(client);
            await client.InitializeAsync(Cancellation);

            connection.ReplaceValues(Values(("a", 10), ("c", 3)));

            client.GetValue("a", 0).ShouldBe(10);
            client.GetValue("b", 0).ShouldBe(0);
            client.GetValue("c", 0).ShouldBe(3);
            watchedB.ShouldBe([2, 0]);
            updates[updates.Count - 1].Keys.ShouldBe(["a", "c"]);
            updates[updates.Count - 1].RemovedKeys.ShouldBe(["b"]);
        }

        [Fact]
        public async Task ChangesBeforeInitializeAreDeliveredByTheFirstAttempt()
        {
            var connection = Connection(Values(("flag", true), ("count", 20)));
            var client = connection.Client;
            var updates = UpdatesOf(client);

            connection.SetValue("greeting", "hello");
            connection.RemoveValue("count");
            await client.InitializeAsync(Cancellation);

            updates.Count.ShouldBe(1);
            updates[0].Keys.ShouldBe(["flag", "greeting"]);
            client.GetValue("greeting", "x").ShouldBe("hello");
            client.GetValue("count", 7).ShouldBe(7);
        }

        [Fact]
        public async Task AnOperationCalledFromAWatchIsDeliveredAfterTheOuterDelivery()
        {
            var connection = Connection(Values(("a", 1), ("b", 1)));
            var client = connection.Client;
            var updates = UpdatesOf(client);
            var order = new List<string>();
            var ready = ReadyCountOf(client);
            client.Watch("a", 0, value =>
            {
                if (value == 2)
                {
                    connection.SetValue("b", 2);
                    order.Add("a-watch-returned");
                }
            });
            client.Watch("b", 0, value => order.Add("b=" + value));
            await client.InitializeAsync(Cancellation);
            order.Clear();

            connection.SetValue("a", 2);

            order.ShouldBe(["a-watch-returned", "b=2"]);
            updates.Count.ShouldBe(3);
            updates[1].Keys.ShouldBe(["a"]);
            updates[2].Keys.ShouldBe(["b"]);
            ready().ShouldBe(1);
            client.GetValue("a", 0).ShouldBe(2);
            client.GetValue("b", 0).ShouldBe(2);
        }

        [Fact]
        public async Task ADeliveryRequestedWhileAnotherThreadDeliversIsMadeByThatThread()
        {
            var connection = Connection(Values(("a", 1), ("b", 1)));
            var client = connection.Client;
            using var watchEntered = new ManualResetEventSlim();
            using var releaseWatch = new ManualResetEventSlim();
            var cancellation = Cancellation;
            var events = new List<string>();
            client.Watch("a", 0, value =>
            {
                if (value == 2)
                {
                    watchEntered.Set();
                    releaseWatch.Wait(Promptly, cancellation);
                    events.Add("a=2 on " + Environment.CurrentManagedThreadId);
                }
            });
            client.Watch("b", 0, value => events.Add("b=" + value + " on " + Environment.CurrentManagedThreadId));
            await client.InitializeAsync(Cancellation);
            events.Clear();
            var outerThread = new Thread(() => connection.SetValue("a", 2));
            outerThread.Start();
            watchEntered.Wait(Promptly, cancellation).ShouldBeTrue();

            connection.SetValue("b", 2);

            events.ShouldBeEmpty();
            releaseWatch.Set();
            outerThread.Join(Promptly).ShouldBeTrue();
            events.ShouldBe(["a=2 on " + outerThread.ManagedThreadId, "b=2 on " + outerThread.ManagedThreadId]);
            client.GetValue("b", 0).ShouldBe(2);
        }

        [Fact]
        public async Task TwoConnectionsNeverShareValues()
        {
            var first = Connection(Values(("flag", true))).Client;
            var second = Connection(Values(("flag", false)));
            await first.InitializeAsync(Cancellation);
            await second.Client.InitializeAsync(Cancellation);

            second.SetValue("flag", true);
            second.SetValue("count", 1);

            first.GetValue("flag", false).ShouldBeTrue();
            first.GetValue("count", 0).ShouldBe(0);
            second.Client.GetValue("count", 0).ShouldBe(1);
        }

        [Fact]
        public async Task AfterDisposeOperationsAreSilent()
        {
            var connection = Connection(Values(("flag", true)));
            var client = connection.Client;
            var watched = new List<bool>();
            client.Watch("flag", false, watched.Add);
            await client.InitializeAsync(Cancellation);
            connection.SetValue("flag", false);

            await client.DisposeAsync();

            Should.NotThrow(() =>
            {
                connection.SetValue("flag", true);
                connection.RemoveValue("flag");
                connection.ReplaceValues(Values(("flag", true)));
                connection.HoldInitialization();
                connection.CompleteInitialization();
                connection.FailInitialization();
            });
            watched.ShouldBe([true, false]);
            connection.IsHoldingAnAttempt.ShouldBeFalse();
        }
    }

    public sealed class Holding : InMemoryConnectionTests
    {
        [Fact]
        public async Task AHeldInitializeCompletesReadyWhenCompleted()
        {
            var connection = Connection(Values(("flag", true)), LongerThanTheTestWaits);
            var client = connection.Client;
            var ready = ReadyCountOf(client);
            connection.HoldInitialization();

            var initializing = client.InitializeAsync(Cancellation);
            connection.IsHoldingAnAttempt.ShouldBeTrue();
            client.IsReady.ShouldBeFalse();

            connection.CompleteInitialization();

            client.IsReady.ShouldBeTrue();
            ready().ShouldBe(1);
            client.GetValue("flag", false).ShouldBeTrue();
            await initializing.WaitAsync(Promptly, Cancellation);
            connection.IsHoldingAnAttempt.ShouldBeFalse();

            connection.SetValue("flag", false);

            client.GetValue("flag", true).ShouldBeFalse();
        }

        [Fact]
        public async Task AValueSetWhileHeldIsDeliveredOnCompletion()
        {
            var connection = Connection(Values(("flag", true)), LongerThanTheTestWaits);
            var client = connection.Client;
            connection.HoldInitialization();
            var initializing = client.InitializeAsync(Cancellation);

            connection.SetValue("flag", false);
            client.IsReady.ShouldBeFalse();
            connection.CompleteInitialization();

            client.GetValue("flag", true).ShouldBeFalse();
            await initializing.WaitAsync(Promptly, Cancellation);
        }

        [Fact]
        public async Task AHeldInitializeThatTimesOutCompletesNotReadyAndUsesTheHoldUp()
        {
            var connection = Connection(Values(("flag", true)), TimeSpan.FromMilliseconds(200));
            var client = connection.Client;
            var ready = ReadyCountOf(client);
            connection.HoldInitialization();

            await client.InitializeAsync(Cancellation);

            client.IsReady.ShouldBeFalse();
            connection.IsHoldingAnAttempt.ShouldBeFalse();
            Warnings().ShouldContain(warning => warning.StartsWith("Timed out waiting for initialization", StringComparison.Ordinal));
            connection.CompleteInitialization();
            client.IsReady.ShouldBeFalse();
            ready().ShouldBe(0);

            await client.InitializeAsync(Cancellation);

            client.IsReady.ShouldBeTrue();
            client.GetValue("flag", false).ShouldBeTrue();
        }

        [Fact]
        public async Task CompletingBeforeInitializePicksTheHoldUpDisarmsIt()
        {
            var connection = Connection(Values(("flag", true)));
            connection.HoldInitialization();
            connection.CompleteInitialization();

            var stopwatch = Stopwatch.StartNew();
            await connection.Client.InitializeAsync(Cancellation);

            connection.Client.IsReady.ShouldBeTrue();
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task ReplaceValuesDisarmsAnArmedHold()
        {
            var connection = Connection(Values(("flag", true)));
            connection.HoldInitialization();

            connection.ReplaceValues(Values(("count", 5)));
            await connection.Client.InitializeAsync(Cancellation);

            connection.Client.IsReady.ShouldBeTrue();
            connection.Client.GetValue("count", 0).ShouldBe(5);
            connection.Client.GetValue("flag", false).ShouldBeFalse();
        }

        [Fact]
        public async Task DisposingTheClientEndsAHeldInitializePromptly()
        {
            var connection = Connection(Values(("flag", true)), LongerThanTheTestWaits);
            var client = connection.Client;
            connection.HoldInitialization();
            var initializing = client.InitializeAsync(Cancellation);
            connection.IsHoldingAnAttempt.ShouldBeTrue();

            await client.DisposeAsync();

            await initializing.WaitAsync(Promptly, Cancellation);
            client.IsReady.ShouldBeFalse();
            connection.IsHoldingAnAttempt.ShouldBeFalse();
        }

        [Fact]
        public async Task ANewAttemptEndsTheHeldOne()
        {
            var connection = Connection(Values(("flag", true)), LongerThanTheTestWaits);
            var client = connection.Client;
            connection.HoldInitialization();
            var held = client.InitializeAsync(Cancellation);

            await client.InitializeAsync(Cancellation);

            client.IsReady.ShouldBeTrue();
            await held.WaitAsync(Promptly, Cancellation);
        }

        [Fact]
        public async Task HoldingTwiceArmsOneHold()
        {
            var connection = Connection(Values(("flag", true)), TimeSpan.FromMilliseconds(200));
            connection.HoldInitialization();
            connection.HoldInitialization();

            await connection.Client.InitializeAsync(Cancellation);
            connection.Client.IsReady.ShouldBeFalse();
            await connection.Client.InitializeAsync(Cancellation);

            connection.Client.IsReady.ShouldBeTrue();
        }
    }

    public sealed class Failing : InMemoryConnectionTests
    {
        [Fact]
        public async Task AnArmedFailureFailsTheNextInitializePromptlyAsAFatalError()
        {
            var connection = Connection(Values(("flag", true)));
            var client = connection.Client;
            var ready = ReadyCountOf(client);
            connection.FailInitialization();

            var stopwatch = Stopwatch.StartNew();
            await client.InitializeAsync(Cancellation);

            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
            client.IsReady.ShouldBeFalse();
            ready().ShouldBe(0);
            Errors().ShouldContain(error =>
                error.Contains("Connection failed with status: 401", StringComparison.Ordinal)
                && error.Contains("unrecoverable", StringComparison.Ordinal));
            Errors().ShouldContain(error => error.StartsWith("Initialization failed", StringComparison.Ordinal));
        }

        [Fact]
        public async Task FailingAHeldInitializeEndsItPromptlyNotReady()
        {
            var connection = Connection(Values(("flag", true)), LongerThanTheTestWaits);
            var client = connection.Client;
            connection.HoldInitialization();
            var initializing = client.InitializeAsync(Cancellation);

            connection.FailInitialization();

            await initializing.WaitAsync(Promptly, Cancellation);
            client.IsReady.ShouldBeFalse();
            connection.IsHoldingAnAttempt.ShouldBeFalse();
            Errors().ShouldContain(error => error.Contains("Connection failed with status: 401", StringComparison.Ordinal));
        }

        [Fact]
        public async Task TheAttemptAfterAFailureSucceedsWithTheStoredValues()
        {
            var connection = Connection(Values(("flag", true)));
            var client = connection.Client;
            connection.FailInitialization();
            await client.InitializeAsync(Cancellation);

            connection.SetValue("count", 4);
            await client.InitializeAsync(Cancellation);

            client.IsReady.ShouldBeTrue();
            client.GetValue("count", 0).ShouldBe(4);
        }

        [Fact]
        public async Task TheLastArmedControlWins()
        {
            var connection = Connection(Values(("flag", true)), TimeSpan.FromMilliseconds(200));
            connection.HoldInitialization();
            connection.FailInitialization();
            await connection.Client.InitializeAsync(Cancellation);
            Errors().ShouldNotBeEmpty();
            _loggerFactory.Logger.Entries.Clear();

            connection.FailInitialization();
            connection.HoldInitialization();
            await connection.Client.InitializeAsync(Cancellation);

            Errors().ShouldBeEmpty();
            connection.Client.IsReady.ShouldBeFalse();
        }

        [Fact]
        public async Task AFailureLeavesTheClientDisconnectedSoALaterSetValueOnlyStores()
        {
            var connection = Connection(Values(("flag", true)));
            var client = connection.Client;
            var watched = new List<bool>();
            client.Watch("flag", false, watched.Add);
            connection.FailInitialization();
            await client.InitializeAsync(Cancellation);

            connection.SetValue("flag", false);

            watched.ShouldBeEmpty();
        }
    }
}
