using System.Text.Json;
using ConfigDirector;

// Settings are read straight from the environment rather than through the configuration system, so
// this sample stays a plain console application with nothing between it and the SDK.
var key = Environment.GetEnvironmentVariable("ConfigDirector__ServerSdkKey") ?? "fake-sample-key";
var url = Environment.GetEnvironmentVariable("ConfigDirector__Url");

await using var client = new ConfigDirectorClient(key, new ConfigDirectorClientOptions
{
    Metadata = new Metadata { AppName = "native-aot-sample", AppVersion = "1.0.0" },
    Connection =
    {
        Mode = ConnectionMode.Polling,
        Timeout = TimeSpan.FromSeconds(3),
        Url = url is null ? null : new Uri(url),
    },
});

await client.InitializeAsync();

var context = new Context
{
    Id = "user-123",
    Traits = { ["plan"] = "pro" },
};

Console.WriteLine($"ready={client.IsReady}");
Console.WriteLine($"temporary-feature-flag={client.GetValue("temporary-feature-flag", false, context)}");
Console.WriteLine($"permanent-kill-switch={client.GetValue("permanent-kill-switch", false, context)}");
Console.WriteLine($"integer-config={client.GetValue("integer-config", 10, context)}");
Console.WriteLine($"day-of-the-week-config={client.GetValue("day-of-the-week-config", "Friday", context)}");

// The AOT-safe way to read a JSON config: JsonElement is parsed by the SDK without reflecting over
// any type of this application's own. GetJsonValue binds to a type you declare instead, which is
// why it carries RequiresUnreferencedCode and would warn here.
var empty = JsonDocument.Parse("{}").RootElement.Clone();
Console.WriteLine($"json-value-config={client.GetValue("json-value-config", empty, context)}");

Console.WriteLine($"configs={client.GetAllConfigs(context).Count}");

// Disposing flushes whatever telemetry is queued, which is what exercises the SDK's source
// generated serializer in a trimmed binary.
