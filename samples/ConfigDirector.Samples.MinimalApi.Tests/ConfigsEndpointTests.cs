using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConfigDirector.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ConfigDirector.Samples.MinimalApi.Tests;

public sealed class ConfigsEndpointTests : IClassFixture<ConfigsEndpointTests.Factory>
{
    private static readonly Dictionary<string, object> SampleValues = new(StringComparer.Ordinal)
    {
        ["temporary-feature-flag"] = false,
        ["permanent-kill-switch"] = true,
        ["integer-config"] = 25,
        ["day-of-the-week-config"] = "Monday",
        ["json-value-config"] = new Dictionary<string, object>(StringComparer.Ordinal) { ["retries"] = 3, ["timeoutMs"] = 1500 },
    };

    private readonly Factory _factory;

    public ConfigsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.TestClient.ReplaceValues(SampleValues);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ServesEveryValueTheTestClientHolds()
    {
        using var http = _factory.CreateClient();

        var configs = await GetConfigs(http, "/configs");

        configs["temporary-feature-flag"].GetBoolean().ShouldBeFalse();
        configs["permanent-kill-switch"].GetBoolean().ShouldBeTrue();
        configs["integer-config"].GetInt32().ShouldBe(25);
        configs["day-of-the-week-config"].GetString().ShouldBe("Monday");
        configs["json-value-config"].GetProperty("retries").GetInt32().ShouldBe(3);
        configs["json-value-config"].GetProperty("timeoutMs").GetInt32().ShouldBe(1500);
    }

    [Fact]
    public async Task AValueSetMidTestIsServedByTheNextRequest()
    {
        using var http = _factory.CreateClient();
        (await GetConfigs(http, "/configs"))["integer-config"].GetInt32().ShouldBe(25);

        _factory.TestClient.SetValue("integer-config", 40);

        (await GetConfigs(http, "/configs"))["integer-config"].GetInt32().ShouldBe(40);
    }

    [Fact]
    public async Task ARemovedValueFallsBackToTheInCodeDefault()
    {
        using var http = _factory.CreateClient();

        _factory.TestClient.RemoveValue("day-of-the-week-config");

        (await GetConfigs(http, "/configs"))["day-of-the-week-config"].GetString().ShouldBe("Friday");
    }

    [Fact]
    public async Task WithNoValuesEveryConfigResolvesToItsInCodeDefault()
    {
        using var http = _factory.CreateClient();

        _factory.TestClient.ReplaceValues(new Dictionary<string, object>(StringComparer.Ordinal));

        var configs = await GetConfigs(http, "/configs");
        configs["temporary-feature-flag"].GetBoolean().ShouldBeTrue();
        configs["permanent-kill-switch"].GetBoolean().ShouldBeFalse();
        configs["integer-config"].GetInt32().ShouldBe(10);
        configs["day-of-the-week-config"].GetString().ShouldBe("Friday");
        configs["json-value-config"].EnumerateObject().Count().ShouldBe(0);
    }

    [Fact]
    public async Task TheQueryStringBecomesTheEvaluationContext()
    {
        using var http = _factory.CreateClient();
        var evaluations = new List<ConfigEvaluation>();
        void Record(object? sender, ConfigEvaluatedEventArgs eventArgs) => evaluations.Add(eventArgs.Evaluation);
        _factory.TestClient.Client.ConfigEvaluated += Record;

        try
        {
            await GetConfigs(http, "/configs?id=user-123&name=Ada&anonymous=true&plan=pro&tags=beta&tags=vip");
        }
        finally
        {
            _factory.TestClient.Client.ConfigEvaluated -= Record;
        }

        evaluations.Select(evaluation => evaluation.Key).ShouldBe(SampleValues.Keys, ignoreOrder: true);
        var expected = new Context
        {
            Id = "user-123",
            Name = "Ada",
            Anonymous = true,
            Traits = { ["plan"] = "pro", ["tags"] = new[] { "beta", "vip" } },
        };
        evaluations.ShouldAllBe(evaluation => evaluation.Context == expected);
    }

    [Fact]
    public async Task ConfigsAllReturnsEveryConfigEvaluated()
    {
        using var http = _factory.CreateClient();

        var configs = await GetConfigs(http, "/configs/all?id=user-123");

        configs.Keys.ShouldBe(SampleValues.Keys, ignoreOrder: true);
        configs["integer-config"].GetProperty("value").GetString().ShouldBe("25");
    }

    [Fact]
    public void TheApplicationIsServedByTheTestClient()
    {
        var client = _factory.Services.GetRequiredService<IConfigDirectorClient>();

        client.ShouldBeSameAs(_factory.TestClient.Client);
    }

    [Fact]
    public async Task HealthReportsTheClientReady()
    {
        using var http = _factory.CreateClient();

        var health = await http.GetFromJsonAsync<JsonElement>(new Uri("/health", UriKind.Relative), Cancellation);

        health.GetProperty("ready").GetBoolean().ShouldBeTrue();
        health.GetProperty("closed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task AnUnknownPathIsNotFound()
    {
        using var http = _factory.CreateClient();

        using var response = await http.GetAsync(new Uri("/nope", UriKind.Relative), Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("error").GetString()
            .ShouldBe("Not found. Try GET /configs");
    }

    [Fact]
    public async Task AFailedInitializationStillStartsTheApplicationServingDefaults()
    {
        await using var testClient = ConfigDirectorTesting.CreateTestClient(SampleValues);
        testClient.FailInitialization();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.ConfigureTestServices(services => services.AddConfigDirectorClient(testClient)));
        using var http = factory.CreateClient();

        var health = await http.GetFromJsonAsync<JsonElement>(new Uri("/health", UriKind.Relative), Cancellation);
        health.GetProperty("ready").GetBoolean().ShouldBeFalse();
        var configs = await GetConfigs(http, "/configs");
        configs["integer-config"].GetInt32().ShouldBe(10);
        configs["day-of-the-week-config"].GetString().ShouldBe("Friday");
    }

    private static async Task<Dictionary<string, JsonElement>> GetConfigs(HttpClient http, string path)
    {
        var configs = await http.GetFromJsonAsync<Dictionary<string, JsonElement>>(new Uri(path, UriKind.Relative), Cancellation);
        return configs.ShouldNotBeNull();
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public TestClient TestClient { get; } = ConfigDirectorTesting.CreateTestClient();

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services => services.AddConfigDirectorClient(TestClient));

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await TestClient.DisposeAsync();
        }
    }
}

internal static class TestClientRegistration
{
    public static void AddConfigDirectorClient(this IServiceCollection services, TestClient testClient)
    {
        services.RemoveAll<IConfigDirectorClient>();
        services.AddSingleton(testClient.Client);
    }
}
