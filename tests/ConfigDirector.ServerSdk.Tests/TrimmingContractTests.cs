using System.Reflection;
using System.Text.Json;
using ConfigDirector.Value;

namespace ConfigDirector.Tests;

// The SDK is trim and AOT clean everywhere except the two members that bind a config to a type of
// the caller's own, which carry RequiresUnreferencedCode. That holds only while every other getter
// deals in types the SDK renders and parses without reflection, so the set of those types is
// pinned here rather than left to be noticed.
public class TrimmingContractTests
{
    private static readonly Type[] HandledWithoutReflection =
    [
        typeof(bool),
        typeof(int),
        typeof(long),
        typeof(double),
        typeof(float),
        typeof(decimal),
        typeof(string),
        typeof(JsonElement),
    ];

    // An overload for a type not in the list above would reach the reflective fallback in
    // TelemetryJson.Serialize, which is suppressed on the grounds that only the annotated
    // GetJsonValue and WatchJson can get there, and would fall out of ValueParser.Parse as a
    // default rather than a value. Both need a case for the new type first.
    [Fact]
    public void EveryGetterDealsInATypeTheSdkHandlesWithoutReflection() =>
        DefaultValueTypesOf("GetValue").ShouldBe(HandledWithoutReflection, ignoreOrder: true);

    [Fact]
    public void EveryWatchDealsInATypeTheSdkHandlesWithoutReflection() =>
        DefaultValueTypesOf("Watch").ShouldBe(HandledWithoutReflection, ignoreOrder: true);

    [Fact]
    public void TheMembersThatBindToACallersTypeSayTheyNeedReflection()
    {
        foreach (var name in new[] { nameof(IConfigDirectorClient.GetJsonValue), nameof(IConfigDirectorClient.WatchJson) })
        {
            var method = typeof(IConfigDirectorClient).GetMethod(name)!;

            method.GetCustomAttributes()
                .Select(attribute => attribute.GetType().Name)
                .ShouldContain("RequiresUnreferencedCodeAttribute", $"{name} is missing the trimming annotation");
        }
    }

    // Reached only through GetJsonValue, which calls Bind. Parse handles the types above and leaves
    // anything else alone rather than reaching for the serializer.
    [Fact]
    public void ParsingATypeOnlyBindingCouldProduceKeepsTheDefault()
    {
        var state = new ConfigState { Key = "k", Value = """{"name":"checkout"}""" };

        var result = ValueParser.Parse(state, new Binding());

        result.Value.Name.ShouldBeNull();
        result.Reason.ShouldBe(EvaluationReason.InvalidJson);
    }

    private static IEnumerable<Type> DefaultValueTypesOf(string name) =>
        typeof(IConfigDirectorClient)
            .GetMethods()
            .Where(method => method.Name == name && !method.IsGenericMethod)
            .Select(method => method.GetParameters().Single(p => p.Name == "defaultValue").ParameterType);

    private sealed record Binding
    {
        public string? Name { get; init; }
    }
}
