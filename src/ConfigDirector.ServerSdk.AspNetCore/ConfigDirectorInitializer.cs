using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ConfigDirector;

// Connects during startup, as IHostedLifecycleService rather than a plain IHostedService: every
// StartingAsync completes before any hosted service is started, and the web host is a hosted
// service registered while the builder is constructed -- long before application code reaches
// AddConfigDirector. A plain IHostedService would therefore run after the server had begun
// listening, and requests arriving in that window would be served defaults.
internal sealed class ConfigDirectorInitializer : IHostedLifecycleService
{
    private readonly IConfigDirectorClient _client;
    private readonly bool _requireReady;
    private readonly ILogger<ConfigDirectorInitializer> _logger;

    public ConfigDirectorInitializer(
        IConfigDirectorClient client,
        IOptions<ConfigDirectorOptions> settings,
        ILogger<ConfigDirectorInitializer> logger)
    {
        _client = client;
        _requireReady = settings.Value.RequireReadyOnStartup;
        _logger = logger;
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await _client.InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (_client.IsReady)
        {
            return;
        }

        if (_requireReady)
        {
            throw new ConfigDirectorConnectionException(
                "No config state arrived from ConfigDirector before the connection timeout elapsed, and "
                + $"{nameof(ConfigDirectorOptions)}.{nameof(ConfigDirectorOptions.RequireReadyOnStartup)} "
                + "is set. Every config would otherwise resolve to the default its caller supplied.");
        }

        Log.NotReady(_logger, null);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static class Log
    {
        internal static readonly Action<ILogger, Exception?> NotReady =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(1, "NotReady"),
                "No config state arrived from ConfigDirector during startup. Configs will return "
                    + "their default value until it does.");
    }
}
