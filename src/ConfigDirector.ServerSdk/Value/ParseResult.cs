namespace ConfigDirector.Value;

internal readonly record struct ParseResult<T>(T Value, EvaluationReason Reason, string? ValueId)
{
    internal bool UsedDefault => Reason != EvaluationReason.FoundMatch;
}
