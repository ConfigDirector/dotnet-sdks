using ConfigDirector.Telemetry;
using ConfigDirector.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigDirector.Tests;

// The server SDK key is a secret. A record prints its public members from the generated ToString,
// so anything holding the key must keep it out of that.
public class SecretExposureTests
{
    private const string Key = "sdk-key-that-must-not-leak";

    [Fact]
    public void TransportOptionsDoNotPrintTheServerSdkKey() =>
        new TransportOptions(Key, new Uri("https://example.test"), _ => { }, NullLoggerFactory.Instance)
            .ToString().ShouldNotContain(Key);

    [Fact]
    public void TelemetryOptionsDoNotPrintTheServerSdkKey() =>
        new TelemetryCollectorOptions(Key, new Uri("https://example.test"), NullLoggerFactory.Instance)
            .ToString().ShouldNotContain(Key);
}
