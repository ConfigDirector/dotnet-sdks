namespace ConfigDirector.Tests;

public class TrackedEventValueTests
{
    [Fact]
    public void TwoWholeNumbersOfDifferentWidthsAreEqual()
    {
        TrackedEventValue fromAnInteger = 3;
        TrackedEventValue fromALong = 3L;

        fromAnInteger.ShouldBe(fromALong);
        fromAnInteger.GetHashCode().ShouldBe(fromALong.GetHashCode());
    }

    [Fact]
    public void TwoDecimalsThatDifferOnlyInTrailingZerosAreEqual()
    {
        TrackedEventValue withTheZero = 49.90m;
        TrackedEventValue withoutIt = 49.9m;

        (withTheZero == withoutIt).ShouldBeTrue();
        withTheZero.GetHashCode().ShouldBe(withoutIt.GetHashCode());
    }

    [Fact]
    public void TheSameNumberInDifferentTypesIsNotEqual()
    {
        TrackedEventValue aDecimal = 49.9m;
        TrackedEventValue aDouble = 49.9;

        (aDecimal != aDouble).ShouldBeTrue();
    }

    [Fact]
    public void ToStringWritesTheNumberAsWritten()
    {
        TrackedEventValue value = 49.90m;

        value.ToString().ShouldBe("49.90");
    }

    [Fact]
    public void AValueNothingWasPutInHasNoNumber()
    {
        default(TrackedEventValue).ToString().ShouldBeEmpty();
    }
}
