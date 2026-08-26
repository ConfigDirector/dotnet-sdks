namespace ConfigDirector.Evaluation;

internal static class ConditionEvaluator
{
    // SEMANTICS.md 1. An attribute the context does not carry is not an error: it resolves to the
    // empty string, so a condition can still match or not match on its own terms. An attribute this
    // SDK version does not know about is different -- there is nothing sensible to compare, so the
    // condition simply does not match.
    internal static bool Evaluate(Condition condition, Context? context, Metadata? metadata)
    {
        if (!TryResolve(condition, context, metadata, out var value))
        {
            return false;
        }

        var targets = condition.TargetValues;

        return condition.TargetType switch
        {
            "text" => TextComparison.Compare(
                TraitText.Render(value), TextComparison.Parse(condition.Operator), targets),
            "number" => NumberComparison.Compare(
                value, NumberComparison.Parse(condition.Operator), targets),
            "semver" => SemverComparison.Compare(
                TraitText.Render(value), SemverComparison.Parse(condition.Operator), targets),
            "datetime" => DateTimeComparison.Compare(
                TraitText.Render(value), DateTimeComparison.Parse(condition.Operator), targets),
            "array" => ArrayComparison.Compare(
                value, ArrayComparison.Parse(condition.Operator), targets),
            _ => false,
        };
    }

    private static bool TryResolve(Condition condition, Context? context, Metadata? metadata, out TraitValue value)
    {
        switch (condition.Attribute)
        {
            case "identifier":
                value = context?.Id;
                return true;
            case "name":
                value = context?.Name;
                return true;
            case "appName":
                value = metadata?.AppName;
                return true;
            case "appVersion":
                value = metadata?.AppVersion;
                return true;
            case "traits":
                value = JsonPointer.Resolve(condition.Trait, context?.TraitsValue ?? TraitValue.Null);
                return true;
            default:
                value = TraitValue.Null;
                return false;
        }
    }
}
