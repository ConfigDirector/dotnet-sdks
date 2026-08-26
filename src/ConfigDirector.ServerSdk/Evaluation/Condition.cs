namespace ConfigDirector.Evaluation;

internal sealed record Condition
{
    private readonly IReadOnlyList<string> _targetValues = [];

    public string Id { get; init; } = string.Empty;

    public string Attribute { get; init; } = string.Empty;

    public string? Trait { get; init; }

    public string Operator { get; init; } = string.Empty;

    public string TargetType { get; init; } = string.Empty;

    public IReadOnlyList<string> TargetValues
    {
        get => _targetValues;
        init => _targetValues = value ?? [];
    }
}
