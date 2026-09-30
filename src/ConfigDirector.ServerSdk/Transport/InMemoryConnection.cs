using ConfigDirector.Evaluation;
using ConfigDirector.Telemetry;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Transport;

internal sealed class InMemoryConnection
{
    private const string TestClientSdkKey = "test-client";
    private const string EnvironmentId = "test-environment";
    private const string ProjectId = "test-project";
    private const int FailedStatus = 401;

    private readonly ILogger _logger;
    private readonly object _stateLock = new();
    private readonly Dictionary<string, Config> _configs = new(StringComparer.Ordinal);
    private readonly object _deliveryLock = new();
    private readonly Queue<ConfigBundle> _pendingDeliveries = new();

    private Action<ConfigBundle>? _onBundle;
    private Armed _armed;
    private HeldAttempt? _heldAttempt;
    private bool _connected;
    private bool _delivering;

    internal InMemoryConnection(
        IReadOnlyDictionary<string, object>? values, TimeSpan? timeout, ILoggerFactory? loggerFactory)
    {
        var options = new ConfigDirectorClientOptions();
        if (loggerFactory is not null)
        {
            options.LoggerFactory = loggerFactory;
        }

        if (timeout is { } configured)
        {
            options.Connection.Timeout = configured;
        }

        _logger = options.LoggerFactory.CreateLogger<InMemoryConnection>();
        foreach (var entry in EncodeAll(values))
        {
            _configs[entry.Key] = entry.Value;
        }

        Client = new ConfigDirectorClient(
            TestClientSdkKey,
            options,
            SdkIdentity.ServerSdk,
            (_, transportOptions) =>
            {
                _onBundle = transportOptions.OnBundle;
                return new Attempts(this);
            },
            _ => DiscardingTelemetryCollector.Instance);
    }

    private enum Armed
    {
        Nothing = 0,
        Hold,
        Failure,
    }

    private enum Outcome
    {
        Completed = 0,
        Failed,
        Ended,
    }

    internal IConfigDirectorClient Client { get; }

    internal bool IsHoldingAnAttempt
    {
        get
        {
            lock (_stateLock)
            {
                return _heldAttempt is not null;
            }
        }
    }

    internal void SetValue(string key, object value)
    {
        var definition = TestValueEncoder.Encode(key, value);
        ConfigBundle? update;
        lock (_stateLock)
        {
            _configs[key] = definition;
            update = _connected ? DeltaUpdate(key, definition) : null;
        }

        DeliverIfAny(update);
    }

    internal void RemoveValue(string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        ConfigBundle? update;
        lock (_stateLock)
        {
            _configs.Remove(key);
            update = _connected ? FullUpdate() : null;
        }

        DeliverIfAny(update);
    }

    internal void ReplaceValues(IReadOnlyDictionary<string, object> values)
    {
        var encoded = EncodeAll(values);
        ConfigBundle? update;
        lock (_stateLock)
        {
            _configs.Clear();
            foreach (var entry in encoded)
            {
                _configs[entry.Key] = entry.Value;
            }

            _armed = Armed.Nothing;
            update = _connected ? FullUpdate() : null;
        }

        DeliverIfAny(update);
    }

    internal void HoldInitialization()
    {
        lock (_stateLock)
        {
            _armed = Armed.Hold;
        }
    }

    internal void CompleteInitialization()
    {
        HeldAttempt? attempt;
        ConfigBundle? update = null;
        lock (_stateLock)
        {
            attempt = _heldAttempt;
            _heldAttempt = null;
            if (attempt is not null)
            {
                _connected = true;
                update = FullUpdate();
            }
            else if (_armed == Armed.Hold)
            {
                _armed = Armed.Nothing;
            }
        }

        if (attempt is not null)
        {
            Deliver(update!);
            attempt.Settle(Outcome.Completed);
        }
    }

    internal void FailInitialization()
    {
        HeldAttempt? attempt;
        lock (_stateLock)
        {
            attempt = _heldAttempt;
            _heldAttempt = null;
            if (attempt is null)
            {
                _armed = Armed.Failure;
            }
        }

        attempt?.Settle(Outcome.Failed);
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        HeldAttempt? previous;
        HeldAttempt? attempt = null;
        Armed pickedUp;
        lock (_stateLock)
        {
            _connected = false;
            previous = _heldAttempt;
            _heldAttempt = null;
            pickedUp = _armed;
            _armed = Armed.Nothing;
            if (pickedUp == Armed.Hold)
            {
                attempt = new HeldAttempt();
                _heldAttempt = attempt;
            }
        }

        previous?.Settle(Outcome.Ended);
        switch (pickedUp)
        {
            case Armed.Failure:
                throw FatalError();
            case Armed.Hold:
                await AwaitHeldAsync(attempt!, cancellationToken).ConfigureAwait(false);
                return;
            default:
                ConnectNow();
                return;
        }
    }

    private async Task AwaitHeldAsync(HeldAttempt attempt, CancellationToken cancellationToken)
    {
        Outcome outcome;
        using (cancellationToken.Register(() => End(attempt)))
        {
            outcome = await attempt.Settled.Task.ConfigureAwait(false);
        }

        switch (outcome)
        {
            case Outcome.Failed:
                throw FatalError();
            case Outcome.Ended:
                throw new OperationCanceledException(cancellationToken);
            default:
                return;
        }
    }

    private void End(HeldAttempt attempt)
    {
        lock (_stateLock)
        {
            if (_heldAttempt != attempt)
            {
                return;
            }

            _heldAttempt = null;
        }

        attempt.Settle(Outcome.Ended);
    }

    private void ConnectNow()
    {
        ConfigBundle update;
        lock (_stateLock)
        {
            _connected = true;
            update = FullUpdate();
        }

        Deliver(update);
    }

    private ValueTask CloseTransportAsync()
    {
        HeldAttempt? attempt;
        lock (_stateLock)
        {
            _connected = false;
            attempt = _heldAttempt;
            _heldAttempt = null;
        }

        attempt?.Settle(Outcome.Ended);
        return default;
    }

    private ConfigDirectorConnectionException FatalError()
    {
        var error = Transports.FatalStatusError(FailedStatus, "the test client failed this initialization");
        Log.Fatal(_logger, error.Message, null);
        return error;
    }

    private ConfigBundle FullUpdate() =>
        new()
        {
            Configs = new Dictionary<string, Config>(_configs, StringComparer.Ordinal),
            Kind = BundleKind.Full,
            EnvironmentId = EnvironmentId,
            ProjectId = ProjectId,
        };

    private static ConfigBundle DeltaUpdate(string key, Config definition) =>
        new()
        {
            Configs = new Dictionary<string, Config>(StringComparer.Ordinal) { [key] = definition },
            Kind = BundleKind.Delta,
            EnvironmentId = EnvironmentId,
            ProjectId = ProjectId,
        };

    private void DeliverIfAny(ConfigBundle? update)
    {
        if (update is not null)
        {
            Deliver(update);
        }
    }

    private void Deliver(ConfigBundle update)
    {
        lock (_deliveryLock)
        {
            _pendingDeliveries.Enqueue(update);
            if (_delivering)
            {
                return;
            }

            _delivering = true;
        }

        for (var next = NextDelivery(); next is not null; next = NextDelivery())
        {
            _onBundle!(next);
        }
    }

    private ConfigBundle? NextDelivery()
    {
        lock (_deliveryLock)
        {
            if (_pendingDeliveries.Count == 0)
            {
                _delivering = false;
                return null;
            }

            return _pendingDeliveries.Dequeue();
        }
    }

    private static Dictionary<string, Config> EncodeAll(IReadOnlyDictionary<string, object>? values)
    {
        var encoded = new Dictionary<string, Config>(StringComparer.Ordinal);
        if (values is null)
        {
            return encoded;
        }

        foreach (var entry in values)
        {
            encoded[entry.Key] = TestValueEncoder.Encode(entry.Key, entry.Value);
        }

        return encoded;
    }

    private sealed class HeldAttempt
    {
        internal TaskCompletionSource<Outcome> Settled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Settle(Outcome outcome) => Settled.TrySetResult(outcome);
    }

    private sealed class Attempts(InMemoryConnection connection) : ITransport
    {
        public Task ConnectAsync(CancellationToken cancellationToken) => connection.ConnectAsync(cancellationToken);

        public ValueTask DisposeAsync() => connection.CloseTransportAsync();
    }

    private static class Log
    {
        internal static readonly Action<ILogger, string, Exception?> Fatal =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(1, "FatalConnectionError"),
                "{Message}");
    }
}
