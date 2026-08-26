using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigDirector.Tests;

public class ConfigDirectorClientOptionsTests
{
    [Fact]
    public void DefaultsToNoMetadataAndNoLogging()
    {
        var options = new ConfigDirectorClientOptions();

        options.Metadata.ShouldBeNull();
        options.LoggerFactory.ShouldBeSameAs(NullLoggerFactory.Instance);
        options.Connection.Mode.ShouldBe(ConnectionMode.Streaming);
    }

    [Fact]
    public void RejectsAMissingLoggerFactory() =>
        Should.Throw<ArgumentNullException>(
            () => new ConfigDirectorClientOptions { LoggerFactory = null! });
}
