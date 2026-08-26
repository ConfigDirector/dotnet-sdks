namespace ConfigDirector.Tests;

public class TraitCollectionTests
{
    [Fact]
    public void StartsEmpty()
    {
        var traits = new TraitCollection();

        traits.Count.ShouldBe(0);
        traits.ShouldBeEmpty();
    }

    [Fact]
    public void KeepsTheLastValueSetForAName()
    {
        var traits = new TraitCollection { ["plan"] = "free" };

        traits["plan"] = "pro";

        traits["plan"].ShouldBe((TraitValue)"pro");
        traits.Count.ShouldBe(1);
    }

    [Fact]
    public void RefusesToAddTheSameNameTwice()
    {
        var traits = new TraitCollection();
        traits.Add("plan", "free");

        Should.Throw<ArgumentException>(() => traits.Add("plan", "pro"));
    }

    [Fact]
    public void ThrowsForANameItDoesNotCarry()
    {
        var traits = new TraitCollection { ["plan"] = "pro" };

        Should.Throw<KeyNotFoundException>(() => traits["missing"]);
        traits.TryGetValue("missing", out var missing).ShouldBeFalse();
        missing.Kind.ShouldBe(TraitValueKind.Null);
    }

    [Fact]
    public void CopiesTheMembersItWasBuiltFrom()
    {
        var source = new Dictionary<string, TraitValue> { ["plan"] = "free" };
        var traits = new TraitCollection(source);

        source["plan"] = "pro";

        traits["plan"].ShouldBe((TraitValue)"free");
    }

    [Fact]
    public void RejectsNullMembers() => Should.Throw<ArgumentNullException>(() => new TraitCollection(null!));

    // Asserted through Equals rather than ShouldBe: the collection is enumerable, so ShouldBe would
    // compare it as a sequence and call two collections holding the same traits in a different
    // order unequal.
    [Fact]
    public void ComparesByValueRegardlessOfOrder()
    {
        var one = new TraitCollection { ["plan"] = "pro", ["age"] = 26 };
        var same = new TraitCollection { ["age"] = 26, ["plan"] = "pro" };
        var different = new TraitCollection { ["plan"] = "pro", ["age"] = 27 };

        one.Equals(same).ShouldBeTrue();
        one.GetHashCode().ShouldBe(same.GetHashCode());
        one.Equals(different).ShouldBeFalse();
        one.Equals(new TraitCollection { ["plan"] = "pro" }).ShouldBeFalse();
        one.Equals(null).ShouldBeFalse();
        one.Equals((object)same).ShouldBeTrue();
        one.Equals("not a collection").ShouldBeFalse();
    }

    [Fact]
    public void ConvertsToATraitValueWithoutCopying()
    {
        TraitValue value = new TraitCollection { ["plan"] = "pro" };

        value.Kind.ShouldBe(TraitValueKind.Object);
        value.TryGetMember("plan", out var plan).ShouldBeTrue();
        plan.ShouldBe((TraitValue)"pro");
        ((TraitValue)(TraitCollection?)null).Kind.ShouldBe(TraitValueKind.Null);
    }

    [Fact]
    public void EnumeratesItsMembers()
    {
        var traits = new TraitCollection { ["plan"] = "pro" };

        traits.Keys.ShouldBe(["plan"]);
        traits.Values.ShouldBe([(TraitValue)"pro"]);
        traits.ShouldHaveSingleItem().Key.ShouldBe("plan");
    }
}
