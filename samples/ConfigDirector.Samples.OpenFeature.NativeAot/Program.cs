using ConfigDirector;
using ConfigDirector.OpenFeature;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

// Settings are read straight from the environment rather than through the configuration system, so
// this sample stays a plain console application with nothing between it and the provider.
var key = Environment.GetEnvironmentVariable("ConfigDirector__ServerSdkKey") ?? "fake-sample-key";
var url = Environment.GetEnvironmentVariable("ConfigDirector__Url");

var provider = new ConfigDirectorProvider(key, new ConfigDirectorClientOptions
{
    Metadata = new ConfigDirector.Metadata { AppName = "openfeature-native-aot-sample", AppVersion = "1.0.0" },
    Connection =
    {
        Mode = ConnectionMode.Polling,
        Timeout = TimeSpan.FromSeconds(3),
        Url = url is null ? null : new Uri(url),
    },
});

// Initializes the provider, which is what connects the ConfigDirector client.
await Api.Instance.SetProviderAsync(provider);
var client = Api.Instance.GetClient();

var context = EvaluationContext.Builder()
    .SetTargetingKey("user-123")
    .Set("traits", Structure.Builder().Set("plan", "pro").Build())
    .Build();

Console.WriteLine($"provider={Api.Instance.GetProviderMetadata()?.Name}");
Console.WriteLine($"ready={client.ProviderStatus == ProviderStatus.Ready}");
Console.WriteLine($"temporary-feature-flag={await client.GetBooleanValueAsync("temporary-feature-flag", false, context)}");
Console.WriteLine($"permanent-kill-switch={await client.GetBooleanValueAsync("permanent-kill-switch", false, context)}");
Console.WriteLine($"integer-config={await client.GetIntegerValueAsync("integer-config", 10, context)}");
Console.WriteLine($"day-of-the-week-config={await client.GetStringValueAsync("day-of-the-week-config", "Friday", context)}");

// A JSON config arrives as an OpenFeature Value, which the provider builds from the SDK's
// JsonElement without reflecting over any type of this application's own.
var settings = await client.GetObjectValueAsync("json-value-config", new Value(Structure.Empty), context);
Console.WriteLine($"json-value-config.keys={string.Join(",", settings.AsStructure?.Keys ?? [])}");

var details = await client.GetBooleanDetailsAsync("temporary-feature-flag", false, context);
Console.WriteLine($"reason={details.Reason} variant={details.Variant}");

// Shutting the API down shuts the provider down, which closes the client and flushes whatever
// telemetry is queued: the part that exercises the SDK's source generated serializer in a trimmed
// binary.
await Api.Instance.ShutdownAsync();
