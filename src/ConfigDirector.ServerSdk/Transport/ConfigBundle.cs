using ConfigDirector.Evaluation;

namespace ConfigDirector.Transport;

internal sealed record ConfigBundle
{
    private readonly IReadOnlyDictionary<string, Config> _configs = new Dictionary<string, Config>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Config> Configs
    {
        get => _configs;
        init => _configs = value ?? new Dictionary<string, Config>(StringComparer.Ordinal);
    }
}
