using Microsoft.Extensions.Logging;

namespace ConfigDirector.Testing;

/// <summary>
/// Settings for a test client, given to
/// <see cref="ConfigDirectorTesting.CreateTestClient(IReadOnlyDictionary{string, object}, TestClientOptions)"/>.
/// </summary>
public sealed class TestClientOptions
{
    /// <summary>
    /// The connection timeout of the SDK client, which bounds how long a held
    /// <see cref="IConfigDirectorClient.InitializeAsync"/> waits. Null means the SDK's production
    /// timeout, <see cref="ConnectionOptions.Timeout"/>.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Where the SDK client writes. Null means the SDK's default,
    /// <see cref="Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory"/>, which discards
    /// everything.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }
}
