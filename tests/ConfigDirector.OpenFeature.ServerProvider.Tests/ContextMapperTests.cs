using OpenFeature.Model;
using OpenFeatureValue = OpenFeature.Model.Value;

namespace ConfigDirector.OpenFeature.Tests;

public class ContextMapperTests
{
    [Fact]
    public void ANullContextStaysNull() => ContextMapper.ToContext(null).ShouldBeNull();

    [Fact]
    public void AnEmptyContextStaysNull() => ContextMapper.ToContext(EvaluationContext.Empty).ShouldBeNull();

    [Fact]
    public void TheTargetingKeyBecomesTheId()
    {
        var context = ContextMapper.ToContext(EvaluationContext.Builder().SetTargetingKey("user-1").Build());

        context!.Id.ShouldBe("user-1");
        context.Name.ShouldBeNull();
        context.Traits.ShouldBeEmpty();
        context.Anonymous.ShouldBeFalse();
    }

    [Fact]
    public void TheTargetingKeyWinsOverAnIdAttribute() =>
        ContextMapper.ToContext(EvaluationContext.Builder().SetTargetingKey("user-1").Set("id", "user-2").Build())!
            .Id.ShouldBe("user-1");

    [Theory]
    [InlineData("user-2", "user-2")]
    [InlineData(42, "42")]
    [InlineData(2.5, "2.5")]
    [InlineData(true, "true")]
    public void AnIdAttributeOfAnyScalarTypeBecomesTheIdAsText(object id, string expected) =>
        ContextMapper.ToContext(EvaluationContext.Builder().Set("id", new OpenFeatureValue(id)).Build())!
            .Id.ShouldBe(expected);

    [Fact]
    public void ADateTimeIdIsRenderedAsIso8601() =>
        ContextMapper.ToContext(EvaluationContext.Builder().Set("id", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)).Build())!
            .Id.ShouldBe("2026-01-02T03:04:05.0000000Z");

    [Fact]
    public void AStructuredIdIsIgnored() =>
        ContextMapper.ToContext(EvaluationContext.Builder().Set("id", Structure.Builder().Set("a", 1).Build()).Build())!
            .Id.ShouldBeNull();

    [Fact]
    public void NameTraitsAndAnonymousMapOntoTheirCounterparts()
    {
        var context = ContextMapper.ToContext(EvaluationContext.Builder()
            .SetTargetingKey("user-1")
            .Set("name", "Ada Lovelace")
            .Set("traits", Structure.Builder()
                .Set("plan", "pro")
                .Set("seats", 12)
                .Set("ratio", 0.5)
                .Set("beta", true)
                .Set("tags", new List<OpenFeatureValue> { new("a"), new("b") })
                .Set("address", Structure.Builder().Set("country", "GB").Build())
                .Build())
            .Set("anonymous", true)
            .Build());

        context!.Name.ShouldBe("Ada Lovelace");
        context.Anonymous.ShouldBeTrue();
        context.Traits["plan"].ShouldBe((TraitValue)"pro");
        context.Traits["seats"].ShouldBe((TraitValue)12L);
        context.Traits["ratio"].ShouldBe((TraitValue)0.5);
        context.Traits["beta"].ShouldBe((TraitValue)true);
        context.Traits["tags"].ShouldBe(TraitValue.FromArray([(TraitValue)"a", (TraitValue)"b"]));
        context.Traits["address"].TryGetMember("country", out var country).ShouldBeTrue();
        country.ShouldBe((TraitValue)"GB");
    }

    [Fact]
    public void AnAnonymousAttributeThatIsNotABooleanIsIgnored() =>
        ContextMapper.ToContext(EvaluationContext.Builder().SetTargetingKey("user-1").Set("anonymous", "yes").Build())!
            .Anonymous.ShouldBeFalse();

    [Fact]
    public void ATraitsAttributeThatIsNotAStructureIsIgnored() =>
        ContextMapper.ToContext(EvaluationContext.Builder().SetTargetingKey("user-1").Set("traits", "pro").Build())!
            .Traits.ShouldBeEmpty();
}
