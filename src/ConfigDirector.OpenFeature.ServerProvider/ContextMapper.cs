using System.Globalization;
using OpenFeature.Model;
using OpenFeatureValue = OpenFeature.Model.Value;

namespace ConfigDirector.OpenFeature;

internal static class ContextMapper
{
    private const string Id = "id";
    private const string Name = "name";
    private const string Traits = "traits";
    private const string Anonymous = "anonymous";

    internal static Context? ToContext(EvaluationContext? evaluationContext)
    {
        if (evaluationContext is null || evaluationContext.Count == 0)
        {
            return null;
        }

        var traits = Attribute(evaluationContext, Traits);
        var anonymous = Attribute(evaluationContext, Anonymous);

        return new Context
        {
            Id = evaluationContext.TargetingKey ?? Text(Attribute(evaluationContext, Id)),
            Name = Text(Attribute(evaluationContext, Name)),
            Traits = traits is { IsStructure: true } ? ValueMapper.ToTraits(traits.AsStructure!) : null!,
            Anonymous = anonymous is { IsBoolean: true } && anonymous.AsBoolean == true,
        };
    }

    private static OpenFeatureValue? Attribute(EvaluationContext evaluationContext, string key) =>
        evaluationContext.TryGetValue(key, out var value) ? value : null;

    internal static string? Text(OpenFeatureValue? value)
    {
        if (value is null || value.IsNull || value.IsStructure || value.IsList)
        {
            return null;
        }

        if (value.IsString)
        {
            return value.AsString;
        }

        if (value.IsBoolean)
        {
            return value.AsBoolean == true ? "true" : "false";
        }

        if (value.IsDateTime)
        {
            return value.AsDateTime!.Value.ToString("o", CultureInfo.InvariantCulture);
        }

        return value.AsDouble!.Value.ToString(CultureInfo.InvariantCulture);
    }
}
