using System.Text.Json;
using System.Text.Json.Nodes;
using OpenFeature.Model;
using OpenFeatureValue = OpenFeature.Model.Value;

namespace ConfigDirector.OpenFeature.Tests;

public class ValueMapperTests
{
    [Fact]
    public void MapsEveryJsonKindOntoAnOpenFeatureValue()
    {
        var element = JsonDocument.Parse("""{"text":"a","whole":3,"fraction":2.5,"yes":true,"no":false,"nothing":null,"list":[1,"b"],"nested":{"k":"v"}}""").RootElement;

        var value = ValueMapper.ToValue(element);

        var structure = value.AsStructure!;
        structure.GetValue("text").AsString.ShouldBe("a");
        structure.GetValue("whole").AsInteger.ShouldBe(3);
        structure.GetValue("fraction").AsDouble.ShouldBe(2.5);
        structure.GetValue("yes").AsBoolean.ShouldBe(true);
        structure.GetValue("no").AsBoolean.ShouldBe(false);
        structure.GetValue("nothing").IsNull.ShouldBeTrue();
        structure.GetValue("list").AsList.ShouldBe([new OpenFeatureValue(1), new OpenFeatureValue("b")]);
        structure.GetValue("nested").AsStructure!.GetValue("k").AsString.ShouldBe("v");
    }

    [Fact]
    public void WritesAnOpenFeatureValueBackAsTheEquivalentJson()
    {
        var value = new OpenFeatureValue(Structure.Builder()
            .Set("text", "a")
            .Set("whole", 3)
            .Set("fraction", 2.5)
            .Set("yes", true)
            .Set("nothing", new OpenFeatureValue())
            .Set("when", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc))
            .Set("list", new List<OpenFeatureValue> { new(1), new("b") })
            .Set("nested", Structure.Builder().Set("k", "v").Build())
            .Build());

        var json = ValueMapper.ToJsonElement(value).GetRawText();

        var expected = """{"text":"a","whole":3,"fraction":2.5,"yes":true,"nothing":null,"when":"2026-01-02T03:04:05Z","list":[1,"b"],"nested":{"k":"v"}}""";
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(expected)).ShouldBeTrue(json);
    }

    [Fact]
    public void RoundTripsAValueThroughJson()
    {
        var value = new OpenFeatureValue(new List<OpenFeatureValue> { new("a"), new(1.5), new(Structure.Builder().Set("k", true).Build()) });

        ValueMapper.ToValue(ValueMapper.ToJsonElement(value)).ShouldBe(value);
    }

    [Fact]
    public void KeepsAWholeNumberIntegralAsATrait()
    {
        ValueMapper.ToTrait(new OpenFeatureValue(42)).ShouldBe((TraitValue)42L);
        ValueMapper.ToTrait(new OpenFeatureValue(2.5)).ShouldBe((TraitValue)2.5);
    }

    [Fact]
    public void ANullValueBecomesANullTrait() => ValueMapper.ToTrait(new OpenFeatureValue()).Kind.ShouldBe(TraitValueKind.Null);
}
