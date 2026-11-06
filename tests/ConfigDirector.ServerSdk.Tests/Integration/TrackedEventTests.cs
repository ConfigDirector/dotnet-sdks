using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Tests.Integration;

public sealed class TrackedEventTests : IDisposable
{
    private const string Emoji = "\U0001F600";

    private readonly SdkServer _server = new();
    private readonly CapturingLoggerFactory _loggerFactory = new();

    [Fact]
    public async Task SendsAnEventTrackedForAContextOnTheNextTelemetryBatchWithItsValueAndDetails()
    {
        await using var client = Client();
        var context = new Context { Id = "user-1" };
        var options = new TrackOptions { Value = 49.9, Details = { ["plan"] = "pro", ["seats"] = 3 } };

        client.Track("checkout-completed", context, options);
        await FlushAsync(client);

        var trackedEvent = TrackedEvents().ShouldHaveSingleItem();
        trackedEvent.GetProperty("name").GetString().ShouldBe("checkout-completed");
        trackedEvent.GetProperty("contextKind").GetString().ShouldBe("user");
        trackedEvent.GetProperty("contextIdentifier").GetString().ShouldBe("user-1");
        trackedEvent.GetProperty("value").GetDouble().ShouldBe(49.9);
        trackedEvent.GetProperty("details").GetRawText().ShouldBe("""{"plan":"pro","seats":3}""");
    }

    [Fact]
    public async Task StampsAnEventWithTheTimeTrackWasCalledRatherThanTheTimeItWasSent()
    {
        await using var client = Client();
        var before = DateTimeOffset.UtcNow;

        client.Track("checkout-completed");
        var after = DateTimeOffset.UtcNow;
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await FlushAsync(client);

        var happenedAt = TrackedEvents().ShouldHaveSingleItem().GetProperty("happenedAt").GetDateTimeOffset();
        happenedAt.ShouldBeInRange(before.AddMilliseconds(-1), after);
    }

    [Fact]
    public async Task SendsAnEventTrackedWithoutAContextWithNoContextKindOrIdentifierAndLogsNothing()
    {
        await using var client = Client();

        client.Track("checkout-completed");
        await FlushAsync(client);

        var trackedEvent = TrackedEvents().ShouldHaveSingleItem();
        trackedEvent.TryGetProperty("contextKind", out _).ShouldBeFalse();
        trackedEvent.TryGetProperty("contextIdentifier", out _).ShouldBeFalse();
        Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task SendsAnEventForAContextWithoutAnIdWithNoContextKindOrIdentifierAndLogsNothing()
    {
        await using var client = Client();

        client.Track("checkout-completed", new Context { Name = "Ada" });
        await FlushAsync(client);

        var trackedEvent = TrackedEvents().ShouldHaveSingleItem();
        trackedEvent.TryGetProperty("contextKind", out _).ShouldBeFalse();
        trackedEvent.TryGetProperty("contextIdentifier", out _).ShouldBeFalse();
        Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task SendsAnEventForAContextWithAnEmptyIdWithNoContextKindOrIdentifier()
    {
        await using var client = Client();

        client.Track("checkout-completed", new Context { Id = string.Empty });
        await FlushAsync(client);

        var trackedEvent = TrackedEvents().ShouldHaveSingleItem();
        trackedEvent.TryGetProperty("contextKind", out _).ShouldBeFalse();
        trackedEvent.TryGetProperty("contextIdentifier", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SendsTheKindOfTheContextAnEventWasTrackedFor()
    {
        await using var client = Client();

        client.Track("checkout-completed", new Context { Id = "device-1", Kind = "device" });
        await FlushAsync(client);

        var trackedEvent = TrackedEvents().ShouldHaveSingleItem();
        trackedEvent.GetProperty("contextKind").GetString().ShouldBe("device");
        trackedEvent.GetProperty("contextIdentifier").GetString().ShouldBe("device-1");
    }

    [Fact]
    public async Task WithholdsTheIdentifierOfAnAnonymousContextFromTheEventTrackedForIt()
    {
        await using var client = Client();

        client.Track("checkout-completed", new Context { Id = "user-1", Anonymous = true });
        await FlushAsync(client);

        var trackedEvent = TrackedEvents().ShouldHaveSingleItem();
        trackedEvent.TryGetProperty("contextKind", out _).ShouldBeFalse();
        trackedEvent.TryGetProperty("contextIdentifier", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task LogsTheInvalidKindOfAContextAsEvaluationsDoAndSendsTheEventWithoutAContext()
    {
        await using var client = Client();

        client.Track("checkout-completed", new Context { Id = "user-1", Kind = "Not Valid" });
        await FlushAsync(client);

        var trackedEvent = TrackedEvents().ShouldHaveSingleItem();
        trackedEvent.TryGetProperty("contextKind", out _).ShouldBeFalse();
        trackedEvent.TryGetProperty("contextIdentifier", out _).ShouldBeFalse();
        var logged = _loggerFactory.Logger.Entries.Where(entry => entry.EventId.Name == "InvalidContextKind").ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Error);
    }

    [Fact]
    public async Task SendsTheEventInTheReportThatCarriesTheInvalidKindOfItsContext()
    {
        await using var client = Client();

        client.Track("checkout-completed", new Context { Id = "user-1", Kind = "Not Valid" });
        await FlushAsync(client);

        _server.Paths.Count(path => path == "/server/telemetry/v1").ShouldBe(1);
        TrackedEvents().ShouldHaveSingleItem();
        var invalidContext = TelemetryPayload().GetProperty("aggregatedEvents").GetProperty("invalidContext");
        invalidContext.GetArrayLength().ShouldBe(1);
        invalidContext[0].GetProperty("event").GetProperty("kindSample").GetString().ShouldBe("Not Valid");
    }

    [Fact]
    public async Task SendsAnEventWhoseNameHas150Characters()
    {
        await using var client = Client();
        var name = new string('n', 150);

        client.Track(name);
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().GetProperty("name").GetString().ShouldBe(name);
    }

    [Fact]
    public async Task CountsACharacterOutsideTheBasicPlaneAsOneCharacterOfTheName()
    {
        await using var client = Client();
        var name = string.Concat(Enumerable.Repeat(Emoji, 150));

        client.Track(name);
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().GetProperty("name").GetString().ShouldBe(name);
    }

    [Fact]
    public async Task DoesNotSendAnEventWhoseNameHas151CharactersAndLogsAWarning()
    {
        await using var client = Client();

        client.Track(new string('n', 151));
        await FlushAsync(client, reportExpected: false);

        _server.Paths.ShouldNotContain("/server/telemetry/v1");
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DoesNotSendAnEventWhoseNameHas151CharactersOutsideTheBasicPlaneAndLogsAWarning()
    {
        await using var client = Client();

        client.Track(string.Concat(Enumerable.Repeat(Emoji, 151)));
        await FlushAsync(client, reportExpected: false);

        _server.Paths.ShouldNotContain("/server/telemetry/v1");
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DoesNotSendAnEventWhoseNameIsEmptyAndLogsAWarning()
    {
        await using var client = Client();

        client.Track(string.Empty);
        await FlushAsync(client, reportExpected: false);

        _server.Paths.ShouldNotContain("/server/telemetry/v1");
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DoesNotSendAnEventWhoseNameIsNullWithoutThrowing()
    {
        await using var client = Client();

        Should.NotThrow(() => client.Track(null!));
        await FlushAsync(client, reportExpected: false);

        _server.Paths.ShouldNotContain("/server/telemetry/v1");
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task SendsADoubleWithEveryDigitItWasGiven()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = 49.123456789012 });
        await FlushAsync(client);

        TelemetryBody().ShouldContain("\"value\":49.123456789012,");
    }

    [Fact]
    public async Task SendsAWholeDoubleAsAWholeNumber()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = 3.0 });
        await FlushAsync(client);

        TelemetryBody().ShouldContain("\"value\":3,");
    }

    [Fact]
    public async Task SendsAValueOfZero()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = 0 });
        await FlushAsync(client);

        TelemetryBody().ShouldContain("\"value\":0,");
    }

    [Fact]
    public async Task SendsAnIntegerAsWritten()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = 3 });
        await FlushAsync(client);

        TelemetryBody().ShouldContain("\"value\":3,");
    }

    [Fact]
    public async Task SendsALongBeyondWhatADoubleHoldsWithoutRoundingIt()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = 9007199254740993 });
        await FlushAsync(client);

        TelemetryBody().ShouldContain("\"value\":9007199254740993,");
    }

    [Fact]
    public async Task SendsADecimalAsItWasWritten()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = 49.90m });
        await FlushAsync(client);

        TelemetryBody().ShouldContain("\"value\":49.90,");
    }

    [Fact]
    public async Task SendsADecimalWithMoreDigitsThanADoubleHoldsWithoutRoundingIt()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = 12345678901234567890.123456789m });
        await FlushAsync(client);

        TelemetryBody().ShouldContain("\"value\":12345678901234567890.123456789,");
    }

    [Fact]
    public async Task SendsAFloatAsItWasWritten()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = 0.1f });
        await FlushAsync(client);

        TelemetryBody().ShouldContain("\"value\":0.1,");
    }

    [Fact]
    public async Task LeavesOutADoubleThatIsNotANumberSendsTheEventAndLogsAWarning()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = double.NaN });
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().TryGetProperty("value", out _).ShouldBeFalse();
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LeavesOutAnInfiniteDoubleSendsTheEventAndLogsAWarning()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = double.PositiveInfinity });
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().TryGetProperty("value", out _).ShouldBeFalse();
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LeavesOutAFloatThatIsNotANumberSendsTheEventAndLogsAWarning()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = float.NaN });
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().TryGetProperty("value", out _).ShouldBeFalse();
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LeavesOutAnInfiniteFloatSendsTheEventAndLogsAWarning()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = float.NegativeInfinity });
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().TryGetProperty("value", out _).ShouldBeFalse();
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LeavesOutAValueNothingWasPutInSendsTheEventAndLogsAWarning()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = default(TrackedEventValue) });
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().TryGetProperty("value", out _).ShouldBeFalse();
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task SendsEmptyDetails()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Details = [] });
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().GetProperty("details").GetRawText().ShouldBe("{}");
        Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task SendsNoDetailsWhenNoneWereGiven()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Value = 3 });
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().TryGetProperty("details", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SendsNoDetailsWhenNullDetailsWereGiven()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Details = null! });
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().TryGetProperty("details", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SendsALoneSurrogateInDetailsAsAReplacementCharacter()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Details = { ["text"] = "a\uD83Db" } });
        await FlushAsync(client);

        TelemetryBody().ShouldContain("\"details\":{\"text\":\"a\uFFFDb\"}");
    }

    [Fact]
    public async Task SendsDetailsWith20TopLevelKeys()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Details = DetailsWithKeys(20) });
        await FlushAsync(client);

        var details = TrackedEvents().ShouldHaveSingleItem().GetProperty("details");
        details.EnumerateObject().Count().ShouldBe(20);
        Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task LeavesOutDetailsWith21TopLevelKeysSendsTheEventAndLogsAWarning()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Details = DetailsWithKeys(21) });
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().TryGetProperty("details", out _).ShouldBeFalse();
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task SendsDetailsOfExactly1024BytesAsJson()
    {
        await using var client = Client();
        var text = new string('x', 1013);

        client.Track("checkout-completed", null, new TrackOptions { Details = { ["text"] = text } });
        await FlushAsync(client);

        TelemetryBody().ShouldContain($$"""
            "details":{"text":"{{text}}"}
            """);
        Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task LeavesOutDetailsOf1025BytesAsJsonSendsTheEventAndLogsAWarning()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Details = { ["text"] = new string('x', 1014) } });
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().TryGetProperty("details", out _).ShouldBeFalse();
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task WritesTextOutsideAsciiInDetailsWithoutEscapingIt()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Details = { ["text"] = $"é+<>&'{Emoji}" } });
        await FlushAsync(client);

        TelemetryBody().ShouldContain($$"""
            "details":{"text":"é+<>&'{{Emoji}}"}
            """);
    }

    [Fact]
    public async Task MeasuresDetailsInBytesSo506TwoByteCharactersAreSent()
    {
        await using var client = Client();
        var text = new string('é', 506);

        client.Track("checkout-completed", null, new TrackOptions { Details = { ["text"] = text } });
        await FlushAsync(client);

        TelemetryBody().ShouldContain($$"""
            "details":{"text":"{{text}}"}
            """);
    }

    [Fact]
    public async Task MeasuresDetailsInBytesSo507TwoByteCharactersAreLeftOut()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Details = { ["text"] = new string('é', 507) } });
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().TryGetProperty("details", out _).ShouldBeFalse();
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task MeasuresACharacterOutsideTheBasicPlaneAtTheFourBytesItIsSentIn()
    {
        await using var client = Client();
        var text = string.Concat(Enumerable.Repeat(Emoji, 253));

        client.Track("checkout-completed", null, new TrackOptions { Details = { ["text"] = text } });
        await FlushAsync(client);

        TelemetryBody().ShouldContain($$"""
            "details":{"text":"{{text}}"}
            """);
        Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task WritesDetailsWithoutSpacesBetweenTheirParts()
    {
        await using var client = Client();
        var options = new TrackOptions { Details = { ["items"] = new[] { 1, 2 }, ["nested"] = new TrackedEventDetails { ["a"] = true } } };

        client.Track("checkout-completed", null, options);
        await FlushAsync(client);

        TelemetryBody().ShouldContain("""
            "details":{"items":[1,2],"nested":{"a":true}}
            """);
    }

    [Fact]
    public async Task SendsStringsNumbersBooleansNullsAndNestedObjectsAndLists()
    {
        await using var client = Client();
        var options = new TrackOptions
        {
            Details =
            {
                ["text"] = "pro",
                ["whole"] = 3,
                ["number"] = 1.5,
                ["flag"] = false,
                ["nothing"] = TrackedEventDetailValue.Null,
                ["texts"] = new[] { "a", null },
                ["wholes"] = new[] { 1L, 2L },
                ["numbers"] = new[] { 1.5, 2.5 },
                ["decimals"] = new[] { 1.50m },
                ["flags"] = new[] { true },
                ["mixed"] = new TrackedEventDetailValue[] { "a", 1, true },
                ["built"] = TrackedEventDetailValue.FromArray([1, 2]),
                ["nested"] = new TrackedEventDetails { ["deep"] = new TrackedEventDetails { ["city"] = "Lisbon" } },
            },
        };

        client.Track("checkout-completed", null, options);
        await FlushAsync(client);

        TelemetryBody().ShouldContain("""
            "details":{"text":"pro","whole":3,"number":1.5,"flag":false,"nothing":null,"texts":["a",null],"wholes":[1,2],"numbers":[1.5,2.5],"decimals":[1.50],"flags":[true],"mixed":["a",1,true],"built":[1,2],"nested":{"deep":{"city":"Lisbon"}}}
            """);
    }

    [Fact]
    public async Task EscapesQuotesBackslashesAndControlCharactersInDetails()
    {
        await using var client = Client();
        var options = new TrackOptions { Details = { ["say \"hi\""] = "back\\slash\b\f\n\r\t\u0001" } };

        client.Track("checkout-completed", null, options);
        await FlushAsync(client);

        var details = TrackedEvents().ShouldHaveSingleItem().GetProperty("details");
        details.GetProperty("say \"hi\"").GetString().ShouldBe("back\\slash\b\f\n\r\t\u0001");
        TelemetryBody().ShouldContain("""
            "details":{"say \"hi\"":"back\\slash\b\f\n\r\t\u0001"}
            """);
    }

    [Fact]
    public async Task SendsADecimalInsideDetailsAsItWasWritten()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Details = { ["total"] = 49.90m } });
        await FlushAsync(client);

        TelemetryBody().ShouldContain("""
            "details":{"total":49.90}
            """);
    }

    [Fact]
    public async Task SendsAFloatInsideDetailsAsItWasWritten()
    {
        await using var client = Client();

        client.Track("checkout-completed", null, new TrackOptions { Details = { ["rate"] = 0.1f } });
        await FlushAsync(client);

        TelemetryBody().ShouldContain("""
            "details":{"rate":0.1}
            """);
    }

    [Fact]
    public async Task SendsANumberThatIsNotFiniteInsideDetailsAsNull()
    {
        await using var client = Client();
        var options = new TrackOptions { Details = { ["ratio"] = double.NaN, ["single"] = float.PositiveInfinity } };

        client.Track("checkout-completed", null, options);
        await FlushAsync(client);

        TelemetryBody().ShouldContain("""
            "details":{"ratio":null,"single":null}
            """);
        Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task LeavesOutDetailsNestedFarBeyondTheLimitWithoutThrowing()
    {
        await using var client = Client();
        var details = new TrackedEventDetails();
        for (var depth = 0; depth < 100_000; depth++)
        {
            details = new TrackedEventDetails { ["nested"] = details };
        }

        Should.NotThrow(() => client.Track("checkout-completed", null, new TrackOptions { Details = details }));
        await FlushAsync(client);

        TrackedEvents().ShouldHaveSingleItem().TryGetProperty("details", out _).ShouldBeFalse();
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task SendsDetailsAssignedToThemselvesAsTheyWereAtTheTime()
    {
        await using var client = Client();
        var details = new TrackedEventDetails { ["plan"] = "pro" };

        details["self"] = details;
        client.Track("checkout-completed", null, new TrackOptions { Details = details });
        await FlushAsync(client);

        TelemetryBody().ShouldContain("""
            "details":{"plan":"pro","self":{"plan":"pro"}}
            """);
    }

    [Fact]
    public async Task SendsTheDetailsAsTheyWereWhenTheEventWasTracked()
    {
        await using var client = Client();
        var options = new TrackOptions { Details = { ["plan"] = "pro" } };

        client.Track("checkout-completed", null, options);
        options.Details["plan"] = "changed";
        await FlushAsync(client);

        TelemetryBody().ShouldContain("""
            "details":{"plan":"pro"}
            """);
    }

    [Fact]
    public async Task Sends1000EventsTrackedBeforeABatchIsSent()
    {
        await using var client = Client();

        for (var index = 0; index < 1_000; index++)
        {
            client.Track($"event-{index}");
        }

        await FlushAsync(client);

        var trackedEvents = TrackedEvents();
        trackedEvents.Length.ShouldBe(1_000);
        trackedEvents[0].GetProperty("name").GetString().ShouldBe("event-0");
        trackedEvents[999].GetProperty("name").GetString().ShouldBe("event-999");
        Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task DoesNotSendThe1001stEventTrackedBeforeABatchIsSentAndLogsAWarning()
    {
        await using var client = Client();

        for (var index = 0; index < 1_001; index++)
        {
            client.Track($"event-{index}");
        }

        await FlushAsync(client);

        var trackedEvents = TrackedEvents();
        trackedEvents.Length.ShouldBe(1_000);
        trackedEvents[999].GetProperty("name").GetString().ShouldBe("event-999");
        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LogsOneWarningHoweverManyEventsGoBeyondTheLimitOfOneBatch()
    {
        await using var client = Client();

        for (var index = 0; index < 1_005; index++)
        {
            client.Track("checkout-completed");
        }

        await FlushAsync(client);

        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LogsOnlyTheWarningAboutTheLimitForAnEventBeyondItWhoseValueWouldBeLeftOut()
    {
        await using var client = Client();
        for (var index = 0; index < 1_000; index++)
        {
            client.Track("checkout-completed");
        }

        client.Track("checkout-completed", null, new TrackOptions { Value = double.NaN });
        await FlushAsync(client);

        Warnings().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LogsEachCauseOfOneBatchOnce()
    {
        await using var client = Client();

        client.Track(string.Empty);
        client.Track("value-left-out", null, new TrackOptions { Value = double.NaN });
        client.Track("details-left-out", null, new TrackOptions { Details = DetailsWithKeys(21) });
        for (var index = 0; index < 1_000; index++)
        {
            client.Track("checkout-completed");
        }

        await FlushAsync(client);

        var warnings = Warnings();
        warnings.Length.ShouldBe(4);
        warnings.Select(warning => warning.Message).Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public async Task LogsNoWarningForAnEventTrackedInOfflineModeThatWouldNotBeReported()
    {
        await using var client = OfflineClient();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.Track(string.Empty, null, new TrackOptions { Value = double.NaN });
        await client.DisposeAsync();

        Warnings().ShouldBeEmpty();
        _server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task LogsNoErrorForAnEventTrackedInOfflineModeForAContextWithAnInvalidKind()
    {
        await using var client = OfflineClient();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.Track("checkout-completed", new Context { Id = "user-1", Kind = "Not Valid" });
        await client.DisposeAsync();

        _loggerFactory.Logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Error);
        _server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task SendsNothingAndLogsNothingForAnEventTrackedAfterDispose()
    {
        var client = Client();
        await client.DisposeAsync();

        client.Track(string.Empty, new Context { Id = "user-1", Kind = "Not Valid" }, new TrackOptions { Value = double.NaN });
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Warnings().ShouldBeEmpty();
        _loggerFactory.Logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Error);
        _server.Paths.ShouldNotContain("/server/telemetry/v1");
    }

    public void Dispose()
    {
        _server.Dispose();
        _loggerFactory.Dispose();
    }

    private static TrackedEventDetails DetailsWithKeys(int count)
    {
        var details = new TrackedEventDetails();
        for (var index = 0; index < count; index++)
        {
            details[$"key-{index}"] = index;
        }

        return details;
    }

    private static async Task WaitAsync(Func<bool> until)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!until() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        until().ShouldBeTrue("the telemetry report never arrived");
    }

    private ConfigDirectorClient Client()
    {
        var options = new ConfigDirectorClientOptions { LoggerFactory = _loggerFactory };
        _server.Attach(options);
        options.Telemetry.FlushInterval = TimeSpan.FromMinutes(5);
        return new ConfigDirectorClient("sdk-key", options);
    }

    private ConfigDirectorClient OfflineClient() =>
        new(new ConfigDirectorClientOptions
        {
            LoggerFactory = _loggerFactory,
            Connection = { Mode = ConnectionMode.Offline, Url = _server.BaseUrl },
        });

    private async Task FlushAsync(ConfigDirectorClient client, bool reportExpected = true)
    {
        await client.DisposeAsync();
        if (reportExpected)
        {
            await WaitAsync(() => _server.Paths.Contains("/server/telemetry/v1"));
        }
    }

    private string TelemetryBody() => _server.Bodies[_server.Paths.IndexOf("/server/telemetry/v1")];

    private JsonElement TelemetryPayload() => JsonDocument.Parse(TelemetryBody()).RootElement.Clone();

    private JsonElement[] TrackedEvents() =>
        [.. TelemetryPayload().GetProperty("discreteEvents").GetProperty("trackedEvents").EnumerateArray()];

    private (LogLevel Level, EventId EventId, string Message, Exception? Error)[] Warnings() =>
        [.. _loggerFactory.Logger.Entries.Where(entry => entry.EventId.Name == "TrackedEventWarning")];
}
