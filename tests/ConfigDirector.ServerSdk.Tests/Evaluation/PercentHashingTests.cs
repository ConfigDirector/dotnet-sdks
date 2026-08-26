using ConfigDirector.Evaluation;

namespace ConfigDirector.Tests.Evaluation;

public class PercentHashingTests
{
    // The bucket assignment, not just the hash: the operands are joined as
    // "<identifier>-<configId>", and joining them the other way round would still hash cleanly
    // while silently putting every user in a different bucket from the other SDKs.
    [Theory]
    [InlineData("config-1", "user-1", 67.8)]
    [InlineData("config-1", "user-2", 57.5)]
    [InlineData("abc", "xyz", 35.2)]
    [InlineData("11111111-1111-4111-8111-111111111111", "10", 8.9)]
    [InlineData("c", "u", 40.0)]
    [InlineData("", "", 20.9)]
    [InlineData("00000000-0000-0000-0000-000000000001", "abc", 61.8)]
    [InlineData("00000000-0000-0000-0000-0000000003e8", "abc", 34.0)]
    [InlineData("00000000-0000-0000-0000-0000000007d0", "378368375", 13.5)]
    [InlineData("00000000-0000-0000-0000-0000000003e8", "378368376", 66.0)]
    public void AssignsTheSameBucketAsTheOtherSdks(string configId, string identifier, double expected) =>
        PercentHashing.AssignPercentage(configId, identifier).ShouldBe(expected);

    [Fact]
    public void AssignsTheSameBucketForTheSamePair() =>
        PercentHashing.AssignPercentage("config-a", "user-1")
            .ShouldBe(PercentHashing.AssignPercentage("config-a", "user-1"));

    // SEMANTICS.md 7.1 -- only 1000 values are reachable, 0.0 through 99.9 in tenths.
    [Fact]
    public void AssignsAPercentageInTenthsInsideTheRange()
    {
        for (var index = 0; index < 1_000; index++)
        {
            var assigned = PercentHashing.AssignPercentage($"config-{index}", $"user-{index}");

            assigned.ShouldBeInRange(0.0, 99.9);
            (assigned * 10).ShouldBe(Math.Round(assigned * 10));
        }
    }

    [Fact]
    public void SpreadsIdentifiersAcrossTheRange()
    {
        var assigned = Enumerable.Range(0, 2_000)
            .Select(index => PercentHashing.AssignPercentage("config-a", $"user-{index}"))
            .Distinct()
            .Count();

        // A hash that collapsed to a constant would still pass the range check above.
        assigned.ShouldBeGreaterThan(500);
    }

    [Fact]
    public void DependsOnBothOperands()
    {
        PercentHashing.AssignPercentage("config-a", "user-1")
            .ShouldNotBe(PercentHashing.AssignPercentage("config-b", "user-1"));
        PercentHashing.AssignPercentage("config-a", "user-1")
            .ShouldNotBe(PercentHashing.AssignPercentage("config-a", "user-2"));
    }
}
