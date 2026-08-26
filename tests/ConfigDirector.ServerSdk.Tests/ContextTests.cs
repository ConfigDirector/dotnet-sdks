namespace ConfigDirector.Tests;

public class ContextTests
{
    [Fact]
    public void CarriesNothingByDefault()
    {
        var context = new Context();

        context.Id.ShouldBeNull();
        context.Name.ShouldBeNull();
        context.Traits.ShouldBeEmpty();
        context.Anonymous.ShouldBeFalse();
    }

    [Fact]
    public void TakesTraitsInline()
    {
        var context = new Context
        {
            Id = "u1",
            Traits =
            {
                ["plan"] = "pro",
                ["age"] = 26,
                ["beta"] = true,
                ["tags"] = new[] { "a", "b" },
                ["scores"] = new[] { 1, 2 },
                ["ratios"] = new[] { 1.5, 2.5 },
                ["flags"] = new[] { true, false },
                ["account"] = new TraitCollection { ["tier"] = 2 },
            },
        };

        context.Traits["plan"].ShouldBe((TraitValue)"pro");
        context.Traits["age"].ShouldBe((TraitValue)26);
        context.Traits["beta"].ShouldBe((TraitValue)true);
        context.Traits["tags"].ShouldBe(TraitValue.FromArray(["a", "b"]));
        context.Traits["scores"].ShouldBe(TraitValue.FromArray([1, 2]));
        context.Traits["ratios"].ShouldBe(TraitValue.FromArray([1.5, 2.5]));
        context.Traits["flags"].ShouldBe(TraitValue.FromArray([true, false]));
        context.Traits["account"].TryGetMember("tier", out var tier).ShouldBeTrue();
        tier.ShouldBe((TraitValue)2);
    }

    [Fact]
    public void TakesAWholeCollectionAtOnce()
    {
        var traits = new TraitCollection { ["plan"] = "free" };
        var context = new Context { Traits = traits };

        traits["plan"] = "pro";

        context.Traits["plan"].ShouldBe((TraitValue)"free");
    }

    [Fact]
    public void TakesADictionaryAtOnce()
    {
        var context = new Context { Traits = new Dictionary<string, TraitValue> { ["plan"] = "pro" } };

        context.Traits["plan"].ShouldBe((TraitValue)"pro");
    }

    [Fact]
    public void ReadsTraitsOrdinally()
    {
        var context = new Context { Traits = { ["Plan"] = "pro" } };

        context.Traits.ContainsKey("Plan").ShouldBeTrue();
        context.Traits.ContainsKey("plan").ShouldBeFalse();
    }

    [Fact]
    public void ComparesByValueIncludingNestedTraits()
    {
        var one = new Context { Id = "u1", Traits = { ["tags"] = new[] { "a", "b" } } };
        var same = new Context { Id = "u1", Traits = { ["tags"] = new[] { "a", "b" } } };
        var different = new Context { Id = "u1", Traits = { ["tags"] = new[] { "a", "c" } } };

        one.ShouldBe(same);
        one.GetHashCode().ShouldBe(same.GetHashCode());
        one.ShouldNotBe(different);
        one.ShouldNotBe(one with { Id = "u2" });
        one.ShouldNotBe(one with { Anonymous = true });
        one.ShouldNotBe(new Context { Id = "u1" });
    }

    [Fact]
    public void KeepsTraitsThroughAWithExpression()
    {
        var context = new Context { Traits = { ["plan"] = "pro" } };

        var identified = context with { Id = "u1" };

        identified.Traits["plan"].ShouldBe((TraitValue)"pro");
    }

    [Fact]
    public void MetadataCarriesNothingByDefault()
    {
        var metadata = new Metadata();

        metadata.AppName.ShouldBeNull();
        metadata.AppVersion.ShouldBeNull();
        new Metadata { AppName = "checkout" }.ShouldNotBe(metadata);
    }
}
