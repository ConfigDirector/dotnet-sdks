namespace ConfigDirector.Evaluation;

internal sealed record Config
{
    private readonly IReadOnlyList<Variation> _variations = [];

    public string Id { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;

    public ConfigType? Type { get; init; }

    public TargetingRules Target { get; init; } = new();

    public IReadOnlyList<Variation> Variations
    {
        get => _variations;
        init => _variations = value ?? [];
    }
}

internal sealed record TargetingRules
{
    private readonly IReadOnlyList<Rule> _rules = [];

    public string? DefaultValue { get; init; }

    public string? DefaultValueId { get; init; }

    public string? DefaultVariationId { get; init; }

    public IReadOnlyList<Rule> Rules
    {
        get => _rules;
        init => _rules = value ?? [];
    }
}
