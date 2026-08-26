namespace ConfigDirector.Samples.AspNetCore;

/// Bound from the "ConfigDirector" section, so a real deployment supplies these as environment
/// variables rather than editing code.
internal sealed class SampleOptions
{
    /// Your server SDK key. A secret: supply it as an environment variable or a user secret.
    public string ServerSdkKey { get; set; } = "fake-sample-key";

    /// Only needed when routing through a proxy to reach ConfigDirector.
    public Uri? Url { get; set; }

    public ConnectionMode Mode { get; set; } = ConnectionMode.Streaming;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);
}
