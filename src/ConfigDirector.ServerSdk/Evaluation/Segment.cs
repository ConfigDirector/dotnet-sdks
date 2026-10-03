namespace ConfigDirector.Evaluation;

internal sealed record Segment
{
    private readonly IReadOnlyList<IReadOnlyList<AttributeCondition>> _groups = [];

    public IReadOnlyList<IReadOnlyList<AttributeCondition>> Groups
    {
        get => _groups;
        init => _groups = value ?? [];
    }
}
