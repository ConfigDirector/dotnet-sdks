using ConfigDirector;
using ConfigDirector.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers a ConfigDirector test client with an application under test.
/// </summary>
public static class ConfigDirectorTestClientServiceCollectionExtensions
{
    private const string PlaceholderServerSdkKey = "test-client";

    /// <summary>
    /// Registers <see cref="TestClient.Client"/> as the application's
    /// <see cref="IConfigDirectorClient"/>, in place of the client <c>AddConfigDirector</c> builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every <see cref="IConfigDirectorClient"/> registration already in the collection is
    /// removed, so it works from <c>WebApplicationFactory.ConfigureTestServices</c>, which runs
    /// after the application's own <c>AddConfigDirector</c>. The container never disposes the
    /// client: the test owns the test client and disposes it.
    /// </para>
    /// <para>
    /// The application's startup initialization stays in place, so host startup calls
    /// <see cref="IConfigDirectorClient.InitializeAsync"/> as in production; the SDK key the
    /// application would need is supplied with a placeholder when none is configured.
    /// </para>
    /// <code>
    /// var factory = new WebApplicationFactory&lt;Program&gt;().WithWebHostBuilder(host =>
    ///     host.ConfigureTestServices(services => services.AddConfigDirectorTestClient(testClient)));
    /// </code>
    /// </remarks>
    /// <param name="services">The collection to register with.</param>
    /// <param name="testClient">The test client whose client the application receives.</param>
    /// <returns>The same collection.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IServiceCollection AddConfigDirectorTestClient(this IServiceCollection services, TestClient testClient)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(testClient);

        services.RemoveAll<IConfigDirectorClient>();
        services.AddSingleton(testClient.Client);
        services.PostConfigure<ConfigDirectorOptions>(SupplyAPlaceholderKey);

        return services;
    }

    private static void SupplyAPlaceholderKey(ConfigDirectorOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerSdkKey))
        {
            settings.ServerSdkKey = PlaceholderServerSdkKey;
        }
    }
}
