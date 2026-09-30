using System.Runtime.CompilerServices;
using ConfigDirector.Transport;

namespace ConfigDirector.Testing;

/// <summary>
/// Entry point to the testing tools.
/// </summary>
/// <remarks>
/// <code>
/// await using var testClient = ConfigDirectorTesting.CreateTestClient(
///     new Dictionary&lt;string, object&gt; { ["new-checkout"] = true });
/// var service = new CheckoutService(testClient.Client);
/// await testClient.Client.InitializeAsync();
///
/// service.IsNewCheckoutEnabled("user-123").ShouldBeTrue();
///
/// testClient.SetValue("new-checkout", false);
/// service.IsNewCheckoutEnabled("user-123").ShouldBeFalse();
/// </code>
/// </remarks>
public static class ConfigDirectorTesting
{
    /// <summary>
    /// Creates a test client: the SDK's real client connected to an in-memory server that the test
    /// controls.
    /// </summary>
    /// <param name="values">
    /// The values to serve, keyed by config key, or null for none. See
    /// <see cref="TestClient.SetValue"/> for the types and how each becomes a config.
    /// </param>
    /// <param name="options">Settings for the test client, or null for the defaults.</param>
    /// <returns>A test client whose <see cref="TestClient.Client"/> has not been initialized yet.</returns>
    /// <exception cref="ArgumentNullException">A value in <paramref name="values"/> is null.</exception>
    /// <exception cref="ArgumentException">A value in <paramref name="values"/> cannot be encoded.</exception>
    /// <exception cref="InvalidOperationException">
    /// This package's version differs from the SDK's. The package relies on the SDK's internals, so
    /// the two must be the same version.
    /// </exception>
    public static TestClient CreateTestClient(
        IReadOnlyDictionary<string, object>? values = null, TestClientOptions? options = null)
    {
        SdkVersionCheck.Verify(
            SdkVersionCheck.VersionOf(typeof(IConfigDirectorClient).Assembly),
            SdkVersionCheck.VersionOf(typeof(ConfigDirectorTesting).Assembly));

        return Build(values, options ?? new TestClientOptions());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TestClient Build(IReadOnlyDictionary<string, object>? values, TestClientOptions options) =>
        new(new InMemoryConnection(values, options.Timeout, options.LoggerFactory));
}
