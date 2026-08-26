using ConfigDirector.Evaluation;

namespace ConfigDirector.Tests.Evaluation;

public class ConditionEvaluatorTests
{
    private static readonly Context User = new()
    {
        Id = "u1",
        Name = "Alejandro",
        Traits =
        {
            ["plan"] = "pro",
            ["age"] = 26,
            ["beta"] = true,
            ["tags"] = new[] { "red", "blue" },
            ["account"] = new TraitCollection { ["tier"] = 2 },
            ["empty"] = TraitValue.Null,
        },
    };

    private static readonly Metadata App = new() { AppName = "checkout", AppVersion = "2.3.4" };

    // SEMANTICS.md 1 -- every attribute the SDK knows resolves before any comparison happens.
    [Theory]
    [InlineData("identifier", "u1")]
    [InlineData("name", "Alejandro")]
    [InlineData("appName", "checkout")]
    [InlineData("appVersion", "2.3.4")]
    public void ResolvesTheNamedAttribute(string attribute, string expected)
    {
        Evaluate(Text(attribute, "=", expected)).ShouldBeTrue();
        Evaluate(Text(attribute, "=", "something else")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("/plan", "pro")]
    [InlineData("/account/tier", "2")]
    [InlineData("/beta", "true")]
    [InlineData("/tags/1", "blue")]
    public void ResolvesATraitThroughItsPointer(string pointer, string expected) =>
        Evaluate(Text("traits", "=", expected) with { Trait = pointer }).ShouldBeTrue();

    // SEMANTICS.md 1 -- an absent value resolves to "", so a negative operator can still match.
    [Theory]
    [InlineData("/missing")]
    [InlineData("/empty")]
    [InlineData("/account/missing")]
    [InlineData("/tags/9")]
    public void ResolvesAnAbsentTraitToEmptyText(string pointer)
    {
        Evaluate(Text("traits", "=", "").WithTrait(pointer)).ShouldBeTrue();
        Evaluate(Text("traits", "does NOT equal", "pro").WithTrait(pointer)).ShouldBeTrue();
        Evaluate(Text("traits", "=", "pro").WithTrait(pointer)).ShouldBeFalse();
    }

    [Fact]
    public void ResolvesATraitsConditionWithNoPointerToEmptyText()
    {
        Evaluate(Text("traits", "=", "")).ShouldBeTrue();
        Evaluate(Text("traits", "=", "").WithTrait("")).ShouldBeTrue();
        Evaluate(Text("traits", "=", "pro")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("identifier")]
    [InlineData("name")]
    [InlineData("appName")]
    [InlineData("appVersion")]
    [InlineData("traits")]
    public void ResolvesEveryAttributeToEmptyTextWithoutAContext(string attribute)
    {
        EvaluateWithoutAContext(Text(attribute, "=", "").WithTrait("/plan")).ShouldBeTrue();
        EvaluateWithoutAContext(Text(attribute, "does NOT equal", "x").WithTrait("/plan")).ShouldBeTrue();
    }

    // SEMANTICS.md 1 -- an attribute this SDK does not know is not compared at all, which is not
    // the same as an absent value: even a negative operator is false.
    [Theory]
    [InlineData("email")]
    [InlineData("Identifier")]
    [InlineData("IDENTIFIER")]
    [InlineData("")]
    [InlineData("id")]
    public void NeverMatchesAnUnknownAttribute(string attribute)
    {
        Evaluate(Text(attribute, "=", "")).ShouldBeFalse();
        Evaluate(Text(attribute, "does NOT equal", "x")).ShouldBeFalse();
        Evaluate(Text(attribute, "is NOT one of")).ShouldBeFalse();
    }

    [Fact]
    public void DispatchesOnTheTargetType()
    {
        Evaluate(Condition("traits", "number", ">", "20").WithTrait("/age")).ShouldBeTrue();
        Evaluate(Condition("appVersion", "semver", ">", "2.0.0")).ShouldBeTrue();
        Evaluate(Condition("traits", "array", "contains any of", "blue").WithTrait("/tags")).ShouldBeTrue();
        Evaluate(Condition("traits", "text", "=", "pro").WithTrait("/plan")).ShouldBeTrue();
    }

    [Fact]
    public void ComparesDatetimes()
    {
        var context = new Context { Traits = { ["signedUpAt"] = "2026-01-28T00:00:00Z" } };

        Evaluate(Condition("traits", "datetime", "is before", "2026-06-01").WithTrait("/signedUpAt"), context)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("TEXT")]
    [InlineData("string")]
    [InlineData("boolean")]
    [InlineData("")]
    public void NeverMatchesAnUnknownTargetType(string targetType)
    {
        Evaluate(Condition("identifier", targetType, "=", "u1")).ShouldBeFalse();
        Evaluate(Condition("identifier", targetType, "does NOT equal", "x")).ShouldBeFalse();
    }

    // An absent value reaches each family in that family's own terms, not as text.
    [Fact]
    public void HandsAnAbsentValueToEachFamilyUnrendered()
    {
        Evaluate(Condition("traits", "number", "!=", "26").WithTrait("/missing")).ShouldBeTrue();
        Evaluate(Condition("traits", "number", "=", "26").WithTrait("/missing")).ShouldBeFalse();
        Evaluate(Condition("traits", "array", "does NOT contain any of", "red").WithTrait("/missing")).ShouldBeTrue();
        Evaluate(Condition("traits", "array", "contains any of", "red").WithTrait("/missing")).ShouldBeFalse();
        Evaluate(Condition("traits", "semver", "is NOT one of", "1.0.0").WithTrait("/missing")).ShouldBeTrue();
        Evaluate(Condition("traits", "datetime", "is before", "2026-01-01").WithTrait("/missing")).ShouldBeFalse();
    }

    // SEMANTICS.md 1.1 -- a structured trait has no text form, so it does not match a text rule.
    [Fact]
    public void ComparesAStructuredTraitAsEmptyText()
    {
        Evaluate(Text("traits", "=", "").WithTrait("/tags")).ShouldBeTrue();
        Evaluate(Text("traits", "=", "").WithTrait("/account")).ShouldBeTrue();
    }

    [Fact]
    public void TreatsMissingTargetValuesAsAnEmptyList()
    {
        var condition = new Condition
        {
            Attribute = "identifier",
            Operator = "is NOT one of",
            TargetType = "text",
            TargetValues = null!,
        };

        Evaluate(condition).ShouldBeTrue();
    }

    private static Condition Text(string attribute, string op, params string[] targets) =>
        Condition(attribute, "text", op, targets);

    private static Condition Condition(string attribute, string targetType, string op, params string[] targets) =>
        new()
        {
            Attribute = attribute,
            Operator = op,
            TargetType = targetType,
            TargetValues = targets,
        };

    private static bool Evaluate(Condition condition, Context? context = null, Metadata? metadata = null) =>
        ConditionEvaluator.Evaluate(condition, context ?? User, metadata ?? App);

    private static bool EvaluateWithoutAContext(Condition condition) =>
        ConditionEvaluator.Evaluate(condition, null, null);
}

internal static class ConditionExtensions
{
    internal static Condition WithTrait(this Condition condition, string pointer) =>
        condition with { Trait = pointer };
}
