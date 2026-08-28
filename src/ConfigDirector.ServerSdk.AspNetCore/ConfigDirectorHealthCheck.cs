using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ConfigDirector;

internal sealed class ConfigDirectorHealthCheck : IHealthCheck
{
    private readonly IConfigDirectorClient _client;

    public ConfigDirectorHealthCheck(IConfigDirectorClient client) => _client = client;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_client.IsClosed)
        {
            // Terminal rather than transient, so this one ignores the registration's failure
            // status: a closed client cannot be reopened, and no later check will pass.
            return Task.FromResult(new HealthCheckResult(
                HealthStatus.Unhealthy,
                "The ConfigDirector client has been closed and cannot be reopened."));
        }

        if (_client.IsReady)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Config state has arrived."));
        }

        // Degraded rather than unhealthy by default. The application still answers every request,
        // with each config resolving to the default its caller supplied, so taking an instance out
        // of rotation is the wrong response to ConfigDirector being unreachable.
        return Task.FromResult(new HealthCheckResult(
            context.Registration.FailureStatus,
            "No config state has arrived from ConfigDirector. Every config is resolving to the "
                + "default its caller supplied."));
    }
}
