using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenFeature;
using OpenFeature.Model;
using OpenFeature.Providers.Memory;

namespace ConfigDirector.Samples.OpenFeature.Tests;

public sealed class ConfigsEndpointTests : IClassFixture<ConfigsEndpointTests.Factory>, IAsyncLifetime
{
    private readonly Factory _factory;

    public ConfigsEndpointTests(Factory factory) => _factory = factory;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => new(_factory.Provider.UpdateFlagsAsync(SampleFlags()));

    public ValueTask DisposeAsync() => default;

    [Fact]
    public async Task ServesEveryFlagTheProviderHolds()
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
    public async Task AFlagUpdatedMidTestIsServedByTheNextRequest()
    {
        using var http = _factory.CreateClient();
        (await GetConfigs(http, "/configs"))["integer-config"].GetInt32().ShouldBe(25);

        var flags = SampleFlags();
        flags["integer-config"] = new Flag<int>(new Dictionary<string, int> { ["forty"] = 40 }, "forty");
        await _factory.Provider.UpdateFlagsAsync(flags);

        (await GetConfigs(http, "/configs"))["integer-config"].GetInt32().ShouldBe(40);
    }

    [Fact]
    public async Task AFlagLeftOutOfAnUpdateFallsBackToTheInCodeDefault()
    {
        using var http = _factory.CreateClient();

        var flags = SampleFlags();
        flags.Remove("day-of-the-week-config");
        await _factory.Provider.UpdateFlagsAsync(flags);

        (await GetConfigs(http, "/configs"))["day-of-the-week-config"].GetString().ShouldBe("Friday");
    }

    [Fact]
    public async Task DetailsReportTheVariantAndTheReason()
    {
        using var http = _factory.CreateClient();

        var details = await GetConfigs(http, "/configs/details");

        var flag = details["temporary-feature-flag"];
        flag.GetProperty("value").GetBoolean().ShouldBeFalse();
        flag.GetProperty("variant").GetString().ShouldBe("off");
        flag.GetProperty("reason").GetString().ShouldBe("STATIC");
        flag.GetProperty("errorType").GetString().ShouldBe("None");
        var missing = details["no-such-config"];
        missing.GetProperty("value").GetString().ShouldBe("fallback");
        missing.GetProperty("reason").GetString().ShouldBe("ERROR");
        missing.GetProperty("errorType").GetString().ShouldBe("FlagNotFound");
    }

    [Fact]
    public async Task TheQueryStringBecomesTheEvaluationContext()
    {
        using var http = _factory.CreateClient();
        EvaluationContext? evaluated = null;
        var flags = SampleFlags();
        flags["temporary-feature-flag"] = new Flag<bool>(
            new Dictionary<string, bool> { ["on"] = true, ["off"] = false },
            "off",
            context =>
            {
                evaluated = context;
                return "on";
            });
        await _factory.Provider.UpdateFlagsAsync(flags);

        var configs = await GetConfigs(http, "/configs?id=user-123&name=Ada&anonymous=true&plan=pro&tags=beta&tags=vip");

        configs["temporary-feature-flag"].GetBoolean().ShouldBeTrue();
        var context = evaluated.ShouldNotBeNull();
        context.TargetingKey.ShouldBe("user-123");
        context.GetValue("name").AsString.ShouldBe("Ada");
        context.GetValue("anonymous").AsBoolean.ShouldBe(true);
        var traits = context.GetValue("traits").AsStructure.ShouldNotBeNull();
        traits.GetValue("plan").AsString.ShouldBe("pro");
        traits.GetValue("tags").AsList.ShouldNotBeNull().Select(tag => tag.AsString).ShouldBe(["beta", "vip"]);
    }

    [Fact]
    public async Task HealthReportsTheInMemoryProviderReady()
    {
        using var http = _factory.CreateClient();

        var health = await http.GetFromJsonAsync<JsonElement>(new Uri("/health", UriKind.Relative), Cancellation);

        health.GetProperty("provider").GetString().ShouldBe("InMemory");
        health.GetProperty("status").GetString().ShouldBe("Ready");
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

    private static Dictionary<string, Flag> SampleFlags() => new(StringComparer.Ordinal)
    {
        ["temporary-feature-flag"] = new Flag<bool>(new Dictionary<string, bool> { ["on"] = true, ["off"] = false }, "off"),
        ["permanent-kill-switch"] = new Flag<bool>(new Dictionary<string, bool> { ["on"] = true, ["off"] = false }, "on"),
        ["integer-config"] = new Flag<int>(new Dictionary<string, int> { ["twenty-five"] = 25 }, "twenty-five"),
        ["day-of-the-week-config"] = new Flag<string>(new Dictionary<string, string> { ["monday"] = "Monday" }, "monday"),
        ["json-value-config"] = new Flag<global::OpenFeature.Model.Value>(
            new Dictionary<string, global::OpenFeature.Model.Value>
            {
                ["settings"] = new global::OpenFeature.Model.Value(Structure.Builder().Set("retries", 3).Set("timeoutMs", 1500).Build()),
            },
            "settings"),
    };

    private static async Task<Dictionary<string, JsonElement>> GetConfigs(HttpClient http, string path)
    {
        var configs = await http.GetFromJsonAsync<Dictionary<string, JsonElement>>(new Uri(path, UriKind.Relative), Cancellation);
        return configs.ShouldNotBeNull();
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public InMemoryProvider Provider { get; } = new(SampleFlags());

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<FeatureProvider>();
                services.AddSingleton<FeatureProvider>(Provider);
            });

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await base.DisposeAsync();
            }
            catch (ChannelClosedException)
            {
            }
        }
    }
}
