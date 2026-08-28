using ConfigDirector;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Reports whether ConfigDirector config state has arrived.
/// </summary>
public static class ConfigDirectorHealthChecksBuilderExtensions
{
    /// <summary>The name the check is registered under when none is given.</summary>
    public const string DefaultName = "configdirector";

    /// <summary>
    /// Adds a health check reporting whether the client holds config state.
    /// </summary>
    /// <remarks>
    /// Reports <see cref="HealthStatus.Degraded"/> rather than
    /// <see cref="HealthStatus.Unhealthy"/> when config state has not arrived: the application
    /// still answers every request, with each config resolving to the default its caller supplied.
    /// Pass <paramref name="failureStatus"/> to say otherwise. A client that has been closed always
    /// reports unhealthy, since it cannot be reopened.
    /// </remarks>
    /// <param name="builder">The builder to add the check to.</param>
    /// <param name="name">The name to register the check under.</param>
    /// <param name="failureStatus">Reported when no config state has arrived.</param>
    /// <param name="tags">Tags for filtering which checks an endpoint runs.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static IHealthChecksBuilder AddConfigDirector(
        this IHealthChecksBuilder builder,
        string name = DefaultName,
        HealthStatus failureStatus = HealthStatus.Degraded,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddCheck<ConfigDirectorHealthCheck>(name, failureStatus, tags ?? []);
    }
}
