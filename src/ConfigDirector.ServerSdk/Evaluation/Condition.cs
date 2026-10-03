namespace ConfigDirector.Evaluation;

internal abstract record Condition
{
    public string Id { get; init; } = string.Empty;
}

internal sealed record AttributeCondition : Condition
{
    private readonly IReadOnlyList<string> _targetValues = [];

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

internal sealed record SegmentCondition : Condition
{
    public string Operator { get; init; } = string.Empty;

    public string SegmentId { get; init; } = string.Empty;
}
