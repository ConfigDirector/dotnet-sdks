namespace ConfigDirector.Tests;

public class TrackedEventDetailsTests
{
    [Fact]
    public void DetailsHoldingTheSameMembersInAnyOrderAreEqual()
    {
        var one = new TrackedEventDetails { ["plan"] = "pro", ["seats"] = 3 };
        var other = new TrackedEventDetails { ["seats"] = 3, ["plan"] = "pro" };

        one.Equals(other).ShouldBeTrue();
        one.GetHashCode().ShouldBe(other.GetHashCode());
    }

    [Fact]
    public void DetailsHoldingADifferentValueAreNotEqual()
    {
        var one = new TrackedEventDetails { ["plan"] = "pro" };
        var other = new TrackedEventDetails { ["plan"] = "free" };

        one.Equals(other).ShouldBeFalse();
    }

    [Fact]
    public void DetailsHoldingAnotherKeyAreNotEqual()
    {
        var one = new TrackedEventDetails { ["plan"] = "pro" };
        var other = new TrackedEventDetails { ["tier"] = "pro" };

        one.Equals(other).ShouldBeFalse();
    }

    [Fact]
    public void NestedObjectsAreComparedByTheirContents()
    {
        TrackedEventDetailValue one = new TrackedEventDetails { ["city"] = "Lisbon" };
        TrackedEventDetailValue other = new TrackedEventDetails { ["city"] = "Lisbon" };

        (one == other).ShouldBeTrue();
        one.GetHashCode().ShouldBe(other.GetHashCode());
    }

    [Fact]
    public void ArraysHoldingTheSameElementsAreEqual()
    {
        TrackedEventDetailValue one = new[] { "a", "b" };
        TrackedEventDetailValue same = new[] { "a", "b" };

        (one == same).ShouldBeTrue();
        one.GetHashCode().ShouldBe(same.GetHashCode());
    }

    [Fact]
    public void ArraysHoldingTheSameElementsInAnotherOrderAreNotEqual()
    {
        TrackedEventDetailValue one = new[] { "a", "b" };
        TrackedEventDetailValue reordered = new[] { "b", "a" };

        (one != reordered).ShouldBeTrue();
    }

    [Fact]
    public void ADoubleThatIsNotANumberIsNull()
    {
        TrackedEventDetailValue notANumber = double.NaN;

        notANumber.ShouldBe(TrackedEventDetailValue.Null);
    }

    [Fact]
    public void AnInfiniteFloatIsNull()
    {
        TrackedEventDetailValue infinite = float.PositiveInfinity;

        infinite.ShouldBe(TrackedEventDetailValue.Null);
    }

    [Fact]
    public void WrappingNestedDetailsCopiesThem()
    {
        var address = new TrackedEventDetails { ["city"] = "Lisbon" };
        var details = new TrackedEventDetails { ["address"] = address };

        address["city"] = "Porto";

        details.ToString().ShouldBe("""{"address":{"city":"Lisbon"}}""");
    }

    [Fact]
    public void BuildingAnArrayCopiesTheValuesGiven()
    {
        var values = new List<TrackedEventDetailValue> { "a" };
        var array = TrackedEventDetailValue.FromArray(values);

        values.Add("b");

        array.ToString().ShouldBe("""["a"]""");
    }

    [Fact]
    public void BuildingAnArrayFromNullIsAnError()
    {
        Should.Throw<ArgumentNullException>(() => TrackedEventDetailValue.FromArray(null!));
    }

    [Fact]
    public void AssigningDetailsToOptionsCopiesThem()
    {
        var details = new TrackedEventDetails { ["plan"] = "pro" };
        var options = new TrackOptions { Details = details };

        details["plan"] = "free";

        options.Details["plan"].ToString().ShouldBe("\"pro\"");
    }

    [Fact]
    public void AssigningNullDetailsToOptionsLeavesThemEmpty()
    {
        var options = new TrackOptions { Details = null! };

        options.Details.Count.ShouldBe(0);
    }

    [Fact]
    public void ToStringWritesTheJsonTheDetailsAreReportedAs()
    {
        var details = new TrackedEventDetails { ["plan"] = "pro", ["total"] = 49.90m, ["tags"] = new[] { "beta" } };

        details.ToString().ShouldBe("""{"plan":"pro","total":49.90,"tags":["beta"]}""");
    }
}
