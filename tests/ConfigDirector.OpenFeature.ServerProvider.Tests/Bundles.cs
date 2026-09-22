namespace ConfigDirector.OpenFeature.Tests;

internal static class Bundles
{
    internal static string Of(params string[] configs) =>
        "{\"kind\":\"full\",\"timestamp\":\"2026-08-01T12:00:00.000Z\",\"configs\":{" + string.Join(",", configs) + "}}";

    internal static string DeltaOf(params string[] configs) =>
        "{\"kind\":\"delta\",\"timestamp\":\"2026-08-01T12:00:01.000Z\",\"configs\":{" + string.Join(",", configs) + "}}";

    internal static string Config(string key, string type, string value, params string[] rules) =>
        Quote(key) + ":{\"id\":" + Quote("id-" + key) + ",\"key\":" + Quote(key) + ",\"type\":" + Quote(type)
        + ",\"target\":{\"defaultValue\":" + value + ",\"defaultValueId\":" + Quote("dv-" + key)
        + ",\"rules\":[" + string.Join(",", rules) + "]}}";

    internal static string Rule(string attribute, string? trait, string target, string value) =>
        "{\"id\":\"r1\",\"type\":\"conditional\",\"order\":1,\"value\":" + Quote(value)
        + ",\"valueId\":\"rule-value\",\"conditions\":[{\"id\":\"c1\",\"attribute\":" + Quote(attribute)
        + ",\"operator\":\"=\",\"targetType\":\"text\",\"targetValues\":[" + Quote(target) + "],\"trait\":"
        + (trait is null ? "null" : Quote(trait)) + "}]}";

    private static string Quote(string text) => "\"" + text + "\"";
}
