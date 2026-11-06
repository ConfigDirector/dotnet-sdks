namespace ConfigDirector.Value;

internal readonly record struct ParseResult<T>(
    T Value,
    EvaluationReason Reason,
    string? ValueId,
    Variation? Variation = null,
    string? RuleId = null,
    string? ShareId = null)
{
    internal bool UsedDefault => Reason != EvaluationReason.FoundMatch;
}
