namespace ConfigDirector.Evaluation;

internal abstract record Rule
{
    public string Id { get; init; } = string.Empty;

    public int? Order { get; init; }
}

internal sealed record ConditionalRule : Rule
{
    private readonly IReadOnlyList<Condition> _conditions = [];
    private readonly IReadOnlyList<PercentageBucket> _percentages = [];

    public string Target { get; init; } = "value";

    public TraitValue Value { get; init; }

    public string? ValueId { get; init; }

    public IReadOnlyList<Condition> Conditions
    {
        get => _conditions;
        init => _conditions = value ?? [];
    }

    public IReadOnlyList<PercentageBucket> Percentages
    {
        get => _percentages;
        init => _percentages = value ?? [];
    }
}

internal sealed record PercentageRule : Rule
{
    private readonly IReadOnlyList<PercentageBucket> _percentages = [];

    public IReadOnlyList<PercentageBucket> Percentages
    {
        get => _percentages;
        init => _percentages = value ?? [];
    }
}

internal sealed record PercentageBucket
{
    public string Id { get; init; } = string.Empty;

    public double Percentage { get; init; }

    public TraitValue Value { get; init; }

    public string? ValueId { get; init; }
}
