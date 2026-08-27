using ConfigDirector.Evaluation;

namespace ConfigDirector.Transport;

internal enum BundleKind
{
    Full = 0,
    Delta,
}

internal sealed record ConfigBundle
{
    private readonly IReadOnlyDictionary<string, Config> _configs = new Dictionary<string, Config>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Config> Configs
    {
        get => _configs;
        init => _configs = value ?? new Dictionary<string, Config>(StringComparer.Ordinal);
    }

    public BundleKind Kind { get; init; }

    public string? EnvironmentId { get; init; }

    public string? ProjectId { get; init; }

    // Echoed back on the next poll so the server can answer with a delta. The server may omit it,
    // in which case every poll returns a full bundle.
    public string? Timestamp { get; init; }
}
