namespace ConfigDirector.Evaluation;

internal abstract record Rule
{
    public string Id { get; init; } = string.Empty;

    public int? Order { get; init; }
}

internal sealed record ConditionalRule : Rule
{
    private readonly IReadOnlyList<Condition> _conditions = [];

    public string Target { get; init; } = "value";

    public TraitValue Value { get; init; }

    public string? ValueId { get; init; }

    public string? VariationId { get; init; }

    public IReadOnlyList<Condition> Conditions
    {
        get => _conditions;
        init => _conditions = value ?? [];
    }

    public IReadOnlyList<PercentageBucket>? Percentages { get; init; }
}

internal sealed record PercentageRule : Rule
{
    public IReadOnlyList<PercentageBucket>? Percentages { get; init; }
}

internal sealed record PercentageBucket
{
    public string Id { get; init; } = string.Empty;

    public double Percentage { get; init; }

    public TraitValue Value { get; init; }

    public string? ValueId { get; init; }

    public string? VariationId { get; init; }
}
