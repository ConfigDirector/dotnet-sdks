using ConfigDirector.Transport;

namespace ConfigDirector.Testing;

/// <summary>
/// A real ConfigDirector client connected to an in-memory server that the test controls.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Client"/> is the SDK's production client: value parsing, targeting evaluation,
/// watches, events, and readiness behave as they do against ConfigDirector. Only the connection
/// and telemetry are replaced, so no request is ever made and nothing is left running after the
/// client is disposed.
/// </para>
/// <para>
/// Every operation that changes values delivers the change on the calling thread before it
/// returns, so watches and <see cref="IConfigDirectorClient.ConfigsUpdated"/> handlers run on the
/// test's thread, and an assertion can follow the call directly.
/// </para>
/// </remarks>
public sealed class TestClient : IAsyncDisposable
{
    private readonly InMemoryConnection _connection;

    internal TestClient(InMemoryConnection connection) => _connection = connection;

    /// <summary>
    /// The SDK client to hand to the code under test. It starts uninitialized, like a production
    /// client; <see cref="IConfigDirectorClient.InitializeAsync"/> completes at once with the
    /// stored values unless initialization is held or armed to fail.
    /// </summary>
    public IConfigDirectorClient Client => _connection.Client;

    internal bool IsHoldingAnAttempt => _connection.IsHoldingAnAttempt;

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/> and, when the client is
    /// connected, delivers it as an update carrying only that key, so reads, watches, and
    /// <see cref="IConfigDirectorClient.ConfigsUpdated"/> handlers see it.
    /// </summary>
    /// <remarks>
    /// The config type follows from the value's runtime type: a <see cref="bool"/>; an integral
    /// type from <see cref="sbyte"/> to <see cref="long"/> (an integer config); a
    /// <see cref="float"/>, <see cref="double"/>, or <see cref="decimal"/> (a float config); a
    /// <see cref="string"/>; or a dictionary with string keys, a list, or a
    /// <see cref="System.Text.Json.JsonElement"/> or <see cref="System.Text.Json.Nodes.JsonNode"/>
    /// holding an object or an array (a JSON config). Every context receives the same value.
    /// </remarks>
    /// <param name="key">The config key, not empty.</param>
    /// <param name="value">The value to serve.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is empty, or <paramref name="value"/> is a non-finite number, of an
    /// unsupported type, or holds JSON contents that cannot be encoded.
    /// </exception>
    public void SetValue(string key, object value) => _connection.SetValue(key, value);

    /// <summary>
    /// Removes <paramref name="key"/> and, when the client is connected, delivers a full update
    /// without it, so reads fall back to the in-code default value and watches of
    /// <paramref name="key"/> receive that default.
    /// </summary>
    /// <param name="key">The config key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public void RemoveValue(string key) => _connection.RemoveValue(key);

    /// <summary>
    /// Replaces every stored value with <paramref name="values"/>, disarms any armed hold or
    /// failure, and, when the client is connected, delivers the new values as a full update. Use
    /// it to reset a test client shared across tests.
    /// </summary>
    /// <param name="values">The values to serve from now on, keyed by config key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> or a value in it is null.</exception>
    /// <exception cref="ArgumentException">
    /// A value cannot be encoded, as for <see cref="SetValue"/>, in which case nothing changes.
    /// </exception>
    public void ReplaceValues(IReadOnlyDictionary<string, object> values)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        _connection.ReplaceValues(values);
    }

    /// <summary>
    /// Makes the next <see cref="IConfigDirectorClient.InitializeAsync"/> wait until
    /// <see cref="CompleteInitialization"/> or <see cref="FailInitialization"/> is called, or until
    /// the client's timeout elapses.
    /// </summary>
    public void HoldInitialization() => _connection.HoldInitialization();

    /// <summary>
    /// Delivers the stored values to a held <see cref="IConfigDirectorClient.InitializeAsync"/>,
    /// on the calling thread, so the client is ready when this returns. When a hold is armed but no
    /// initialization has started, it disarms the hold instead.
    /// </summary>
    public void CompleteInitialization() => _connection.CompleteInitialization();

    /// <summary>
    /// Fails a held <see cref="IConfigDirectorClient.InitializeAsync"/> with an unrecoverable
    /// connection error, or arms the next one to fail. Initialization completes promptly with the
    /// client not ready, and the error is logged.
    /// </summary>
    public void FailInitialization() => _connection.FailInitialization();

    /// <summary>Disposes the client. Disposing twice is harmless.</summary>
    /// <returns>A task that completes once the client is disposed.</returns>
    public ValueTask DisposeAsync() => _connection.Client.DisposeAsync();
}
