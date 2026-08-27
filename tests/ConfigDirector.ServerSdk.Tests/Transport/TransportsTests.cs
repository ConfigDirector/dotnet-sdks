using ConfigDirector.Transport;

namespace ConfigDirector.Tests.Transport;

public class TransportsTests
{
    [Theory]
    [InlineData("https://api.example.com", "https://api.example.com/server/polling/v1")]
    [InlineData("https://api.example.com/", "https://api.example.com/server/polling/v1")]
    // A proxy base URL carries a path, and its last segment is a directory rather than a file.
    [InlineData("https://proxy.example.com/configdirector", "https://proxy.example.com/configdirector/server/polling/v1")]
    [InlineData("https://proxy.example.com/configdirector/", "https://proxy.example.com/configdirector/server/polling/v1")]
    public void ResolvesAPathAgainstTheBaseUrl(string baseUrl, string expected) =>
        Transports.Resolve(new Uri(baseUrl), "server/polling/v1").ShouldBe(new Uri(expected));

    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(2, 2, 4)]
    [InlineData(9, 256, 512)]
    public void GrowsTheBackoffWithEveryAttempt(int attempt, int floorSeconds, int ceilingSeconds)
    {
        var random = new Random(1);

        for (var draw = 0; draw < 200; draw++)
        {
            var delay = Transports.BackoffDelay(attempt, random);

            delay.TotalSeconds.ShouldBeGreaterThanOrEqualTo(floorSeconds);
            delay.TotalSeconds.ShouldBeLessThan(ceilingSeconds);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void TreatsAnAttemptBelowOneAsTheFirst(int attempt) =>
        Transports.BackoffDelay(attempt, new Random(1)).TotalSeconds.ShouldBeGreaterThanOrEqualTo(1);

    [Fact]
    public void CapsTheBackoffRatherThanLettingItGrowForever() =>
        Transports.BackoffDelay(40, new Random(1)).TotalSeconds.ShouldBeLessThan(512);

    [Fact]
    public void SpreadsTheDelayAcrossItsRange()
    {
        var random = new Random(7);
        var drawn = Enumerable.Range(0, 200)
            .Select(_ => Transports.BackoffDelay(3, random).TotalSeconds)
            .ToList();

        // Half of each delay is fixed and half is drawn, so the spread is real but bounded.
        drawn.Distinct().Count().ShouldBeGreaterThan(100);
        drawn.Min().ShouldBeGreaterThanOrEqualTo(4);
        drawn.Max().ShouldBeLessThan(8);
    }

    [Theory]
    [InlineData(400, true)]
    [InlineData(401, true)]
    [InlineData(499, true)]
    [InlineData(500, false)]
    [InlineData(503, false)]
    [InlineData(200, false)]
    public void TreatsOnlyAClientErrorAsUnrecoverable(int status, bool fatal) =>
        Transports.IsFatalStatus(status).ShouldBe(fatal);
}
