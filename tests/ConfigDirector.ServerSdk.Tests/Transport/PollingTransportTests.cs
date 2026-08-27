using System.Net;
using System.Text.Json;
using ConfigDirector.Tests.EventSource;
using ConfigDirector.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigDirector.Tests.Transport;

public class PollingTransportTests
{
    private static readonly Uri BaseUrl = new("https://server-sdk-api.example.com/");

    private const string FullBundle = """
        {
          "environmentId": "env-1",
          "kind": "full",
          "timestamp": "2024-01-01T00:00:00.000Z",
          "configs": {
            "integer-config": { "id": "i", "key": "integer-config", "type": "integer",
                                "target": { "defaultValue": 25 } }
          }
        }
        """;

    [Fact]
    public async Task PostsToThePollingEndpointWithTheKeyAndSdkIdentity()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Json(FullBundle));
        await using var transport = Polling(handler, TimeSpan.Zero, new Metadata { AppName = "checkout", AppVersion = "1.2.3" });

        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        handler.Requests[0].Method.ShouldBe(HttpMethod.Post);
        handler.Requests[0].RequestUri.ShouldBe(new Uri("https://server-sdk-api.example.com/server/polling/v1"));
        handler.Requests[0].Headers.GetValues("User-Agent").ShouldBe([$"dotnet-server-sdk/{SdkVersion()}"]);

        using var payload = JsonDocument.Parse(handler.Bodies[0]!);
        payload.RootElement.GetProperty("serverSdkKey").GetString().ShouldBe("sdk-key");
        var meta = payload.RootElement.GetProperty("metaContext");
        meta.GetProperty("sdkName").GetString().ShouldBe("dotnet-server-sdk");
        meta.GetProperty("sdkVersion").GetString().ShouldNotBeNullOrEmpty();
        meta.GetProperty("appName").GetString().ShouldBe("checkout");
        meta.GetProperty("appVersion").GetString().ShouldBe("1.2.3");
        payload.RootElement.TryGetProperty("lastUpdateTimestamp", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task LeavesOutMetadataItWasNotGiven()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Json(FullBundle));
        await using var transport = Polling(handler, TimeSpan.Zero);

        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        using var payload = JsonDocument.Parse(handler.Bodies[0]!);
        var meta = payload.RootElement.GetProperty("metaContext");
        meta.TryGetProperty("appName", out _).ShouldBeFalse();
        meta.TryGetProperty("appVersion", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task PublishesTheBundleItFetched()
    {
        var bundles = new List<ConfigBundle>();
        var handler = new ScriptedHandler(() => ScriptedHandler.Json(FullBundle));
        await using var transport = Polling(handler, TimeSpan.Zero, onBundle: bundles.Add);

        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var bundle = bundles.ShouldHaveSingleItem();
        bundle.EnvironmentId.ShouldBe("env-1");
        bundle.Configs["integer-config"].Target.DefaultValue.ShouldBe("25");
    }

    [Fact]
    public async Task EchoesTheTimestampBackSoTheServerCanAnswerWithADelta()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Json(FullBundle));
        await using var transport = Polling(handler, TimeSpan.FromMilliseconds(30));

        await transport.ConnectAsync(TestContext.Current.CancellationToken);
        await WaitForRequestsAsync(handler, 2);

        using var second = JsonDocument.Parse(handler.Bodies[1]!);
        second.RootElement.GetProperty("lastUpdateTimestamp").GetString()
            .ShouldBe("2024-01-01T00:00:00.000Z");
    }

    [Fact]
    public async Task PublishesNothingWhenTheServerHasNothingNewer()
    {
        var bundles = new List<ConfigBundle>();
        var handler = new ScriptedHandler(() => ScriptedHandler.Status(HttpStatusCode.NoContent));
        await using var transport = Polling(handler, TimeSpan.Zero, onBundle: bundles.Add);

        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        bundles.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivesUpOnAStatusItCannotRecoverFrom()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Status(HttpStatusCode.Unauthorized, "bad key"));
        await using var transport = Polling(handler, TimeSpan.FromMilliseconds(20));

        var failure = await Should.ThrowAsync<ConfigDirectorConnectionException>(
            transport.ConnectAsync(TestContext.Current.CancellationToken));

        failure.Status.ShouldBe(401);
        failure.Message.ShouldContain("unrecoverable");

        // Polling never starts, so the count stays where it was.
        await Task.Delay(80, TestContext.Current.CancellationToken);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RefusesToReconnectAfterAnUnrecoverableError()
    {
        var logger = new CapturingLoggerFactory();
        var handler = new ScriptedHandler(() => ScriptedHandler.Status(HttpStatusCode.Unauthorized, "bad key"));
        await using var transport = new PollingTransport(
            new TransportOptions("sdk-key", BaseUrl, new HttpClient(handler), _ => { }, logger)
            {
                PollingInterval = TimeSpan.Zero,
            });

        await Should.ThrowAsync<ConfigDirectorConnectionException>(
            transport.ConnectAsync(TestContext.Current.CancellationToken));
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        handler.Requests.Count.ShouldBe(1);
        logger.Logger.Entries.ShouldContain(entry =>
            entry.Message.Contains("Ignoring the attempt to reconnect", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeepsPollingAfterAStatusThatMightRecover()
    {
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable, "later"),
            () => ScriptedHandler.Json(FullBundle));
        var bundles = new List<ConfigBundle>();
        await using var transport = Polling(handler, TimeSpan.FromMilliseconds(20), onBundle: bundles.Add);

        var failure = await Should.ThrowAsync<ConfigDirectorConnectionException>(
            transport.ConnectAsync(TestContext.Current.CancellationToken));
        failure.Status.ShouldBe(503);

        await WaitForAsync(() => bundles.Count > 0);
    }

    [Fact]
    public async Task ReportsAConnectionThatNeverAnswered()
    {
        var handler = new ScriptedHandler(ScriptedHandler.Throws());
        await using var transport = Polling(handler, TimeSpan.Zero);

        var failure = await Should.ThrowAsync<ConfigDirectorConnectionException>(
            transport.ConnectAsync(TestContext.Current.CancellationToken));

        failure.Status.ShouldBeNull();
        failure.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task ReportsAResponseItCouldNotRead()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Json("not json at all"));
        await using var transport = Polling(handler, TimeSpan.Zero);

        var failure = await Should.ThrowAsync<ConfigDirectorConnectionException>(
            transport.ConnectAsync(TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("parse");
    }

    [Fact]
    public async Task KeepsFetchingOnTheInterval()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Json(FullBundle));
        await using var transport = Polling(handler, TimeSpan.FromMilliseconds(30));

        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        await WaitForRequestsAsync(handler, 3);
    }

    [Fact]
    public async Task StopsFetchingOnceDisposed()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Json(FullBundle));
        var transport = Polling(handler, TimeSpan.FromMilliseconds(20));

        await transport.ConnectAsync(TestContext.Current.CancellationToken);
        await WaitForRequestsAsync(handler, 2);
        await transport.DisposeAsync();

        var settled = handler.Requests.Count;
        await Task.Delay(120, TestContext.Current.CancellationToken);
        handler.Requests.Count.ShouldBe(settled);
    }

    [Fact]
    public async Task OneTimeFetchesOnceAndNeverAgain()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Json(FullBundle));
        await using var transport = new OneTimeTransport(Options(handler, TimeSpan.FromMilliseconds(20), null, _ => { }));

        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        await Task.Delay(120, TestContext.Current.CancellationToken);
        handler.Requests.Count.ShouldBe(1);
    }

    private static string SdkVersion()
    {
        using var payload = JsonDocument.Parse("{}");
        return typeof(ConfigDirectorClient).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Select(attribute => attribute.InformationalVersion.Split('+')[0])
            .FirstOrDefault() ?? "0.0.0-dev";
    }

    private static PollingTransport Polling(
        ScriptedHandler handler,
        TimeSpan interval,
        Metadata? metadata = null,
        Action<ConfigBundle>? onBundle = null) =>
        new(Options(handler, interval, metadata, onBundle ?? (_ => { })));

    private static TransportOptions Options(
        ScriptedHandler handler,
        TimeSpan interval,
        Metadata? metadata,
        Action<ConfigBundle> onBundle) =>
        new("sdk-key", BaseUrl, new HttpClient(handler), onBundle, NullLoggerFactory.Instance)
        {
            Metadata = metadata,
            PollingInterval = interval,
        };

    private static Task WaitForRequestsAsync(ScriptedHandler handler, int count) =>
        WaitForAsync(() => handler.Requests.Count >= count);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The condition was never met.");
    }
}
