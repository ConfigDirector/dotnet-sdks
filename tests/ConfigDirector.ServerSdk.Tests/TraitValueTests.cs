namespace ConfigDirector.Tests;

public class TraitValueTests
{
    [Fact]
    public void DefaultValueIsNull()
    {
        default(TraitValue).Kind.ShouldBe(TraitValueKind.Null);
        TraitValue.Null.Kind.ShouldBe(TraitValueKind.Null);
    }

    [Fact]
    public void ConvertsScalars()
    {
        ((TraitValue)"abc").Kind.ShouldBe(TraitValueKind.String);
        ((TraitValue)true).Kind.ShouldBe(TraitValueKind.Boolean);
        ((TraitValue)26).Kind.ShouldBe(TraitValueKind.Number);
        ((TraitValue)26L).Kind.ShouldBe(TraitValueKind.Number);
        ((TraitValue)26.5).Kind.ShouldBe(TraitValueKind.Number);
        ((TraitValue)26.5f).Kind.ShouldBe(TraitValueKind.Number);
    }

    [Fact]
    public void ConvertsNullStringToNull()
    {
        ((TraitValue)(string?)null).Kind.ShouldBe(TraitValueKind.Null);
        ((TraitValue)(TraitValue[]?)null).Kind.ShouldBe(TraitValueKind.Null);
    }

    [Fact]
    public void ConvertsArrays()
    {
        TraitValue value = new TraitValue[] { 1, "two", true };

        value.Kind.ShouldBe(TraitValueKind.Array);
        value.Elements.Count.ShouldBe(3);
        value.Elements[1].ShouldBe((TraitValue)"two");
    }

    [Fact]
    public void CopiesTheArrayItWasBuiltFrom()
    {
        var elements = new TraitValue[] { 1, 2 };
        var value = TraitValue.FromArray(elements);

        elements[0] = 99;

        value.Elements[0].ShouldBe((TraitValue)1);
    }

    [Fact]
    public void CopiesTheMembersItWasBuiltFrom()
    {
        var members = new Dictionary<string, TraitValue> { ["plan"] = "free" };
        var value = TraitValue.FromObject(members);

        members["plan"] = "pro";

        value.TryGetMember("plan", out var member).ShouldBeTrue();
        member.ShouldBe((TraitValue)"free");
    }

    [Fact]
    public void RejectsNullCollections()
    {
        Should.Throw<ArgumentNullException>(() => TraitValue.FromArray(null!));
        Should.Throw<ArgumentNullException>(() => TraitValue.FromObject(null!));
    }

    [Fact]
    public void ReadsMembersOrdinally()
    {
        var value = TraitValue.FromObject(new Dictionary<string, TraitValue> { ["Plan"] = "pro" });

        value.TryGetMember("Plan", out var found).ShouldBeTrue();
        found.ShouldBe((TraitValue)"pro");
        value.TryGetMember("plan", out var missing).ShouldBeFalse();
        missing.Kind.ShouldBe(TraitValueKind.Null);
    }

    [Fact]
    public void ReadsElementsWithinRange()
    {
        TraitValue value = new TraitValue[] { "a", "b" };

        value.TryGetElement(1, out var found).ShouldBeTrue();
        found.ShouldBe((TraitValue)"b");
        value.TryGetElement(2, out _).ShouldBeFalse();
        value.TryGetElement(-1, out _).ShouldBeFalse();
    }

    [Fact]
    public void HasNoElementsWhenNotAnArray()
    {
        ((TraitValue)"abc").Elements.ShouldBeEmpty();
        TraitValue.Null.Elements.ShouldBeEmpty();
    }

    [Fact]
    public void ComparesScalarsByValue()
    {
        ((TraitValue)"abc").ShouldBe((TraitValue)"abc");
        ((TraitValue)"abc").ShouldNotBe((TraitValue)"abd");
        ((TraitValue)true).ShouldNotBe((TraitValue)false);
        ((TraitValue)26).ShouldBe((TraitValue)26.0);
        ((TraitValue)26).ShouldNotBe((TraitValue)"26");
        ((TraitValue)0).ShouldNotBe((TraitValue)false);
        TraitValue.Null.ShouldBe(default(TraitValue));
    }

    [Fact]
    public void ComparesArraysAndObjectsStructurally()
    {
        TraitValue.FromArray([1, 2]).ShouldBe(TraitValue.FromArray([1, 2]));
        TraitValue.FromArray([1, 2]).ShouldNotBe(TraitValue.FromArray([1, 3]));
        TraitValue.FromArray([1]).ShouldNotBe(TraitValue.FromArray([1, 2]));

        TraitValue nested = new TraitValue[] { TraitValue.FromObject(new Dictionary<string, TraitValue> { ["a"] = 1 }) };
        TraitValue same = new TraitValue[] { TraitValue.FromObject(new Dictionary<string, TraitValue> { ["a"] = 1 }) };
        nested.ShouldBe(same);
    }

    [Fact]
    public void HashesEqualValuesAlike()
    {
        ((TraitValue)26).GetHashCode().ShouldBe(((TraitValue)26.0).GetHashCode());
        TraitValue.FromArray([1, "a"]).GetHashCode().ShouldBe(TraitValue.FromArray([1, "a"]).GetHashCode());
        TraitValue.FromObject(new Dictionary<string, TraitValue> { ["a"] = 1 })
            .GetHashCode()
            .ShouldBe(TraitValue.FromObject(new Dictionary<string, TraitValue> { ["a"] = 1 }).GetHashCode());
    }

    [Fact]
    public void SupportsEqualityOperators()
    {
        ((TraitValue)"a" == (TraitValue)"a").ShouldBeTrue();
        ((TraitValue)"a" != (TraitValue)"b").ShouldBeTrue();
    }
}
