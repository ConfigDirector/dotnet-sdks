namespace ConfigDirector.Tests;

public class ConnectionOptionsTests
{
    [Fact]
    public void DefaultsToStreamingWithAThreeSecondTimeout()
    {
        var options = new ConnectionOptions();

        options.Mode.ShouldBe(ConnectionMode.Streaming);
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(3));
        options.PollingInterval.ShouldBe(TimeSpan.FromMinutes(5));
        options.Url.ShouldBeNull();
    }

    [Fact]
    public void PollsEveryFiveMinutesWhenNoIntervalIsGiven() =>
        ConnectionOptions.DefaultPollingInterval.ShouldBe(TimeSpan.FromMinutes(5));

    [Theory]
    [InlineData(1)]
    [InlineData(59)]
    public void RejectsAPollingIntervalShorterThanAMinute(int seconds) =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ConnectionOptions { PollingInterval = TimeSpan.FromSeconds(seconds) });

    [Fact]
    public void RejectsAPollingIntervalJustUnderAMinute() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ConnectionOptions { PollingInterval = TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1) });

    [Fact]
    public void AcceptsAPollingIntervalOfExactlyAMinute()
    {
        var minute = TimeSpan.FromMinutes(1);

        new ConnectionOptions { PollingInterval = minute }.PollingInterval.ShouldBe(minute);
        ConnectionOptions.MinPollingInterval.ShouldBe(minute);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsATimeoutThatIsNotPositive(int seconds) =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ConnectionOptions { Timeout = TimeSpan.FromSeconds(seconds) });

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsAPollingIntervalThatIsNotPositive(int seconds) =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ConnectionOptions { PollingInterval = TimeSpan.FromSeconds(seconds) });

    [Fact]
    public void RejectsATimeoutLongerThanTheSdkCanWaitOut() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ConnectionOptions { Timeout = TimeSpan.FromMilliseconds(int.MaxValue) + TimeSpan.FromMilliseconds(1) });

    [Fact]
    public void RejectsAPollingIntervalLongerThanTheSdkCanWaitOut() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ConnectionOptions { PollingInterval = TimeSpan.FromDays(30) });

    [Fact]
    public void AcceptsTheLongestDurationItCanWaitOut()
    {
        var longest = TimeSpan.FromMilliseconds(int.MaxValue);

        new ConnectionOptions { Timeout = longest }.Timeout.ShouldBe(longest);
    }

    [Fact]
    public void RejectsAModeThatIsNotOneOfTheDefinedModes() =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => new ConnectionOptions { Mode = (ConnectionMode)42 });

    [Fact]
    public void RejectsARelativeUrl() =>
        Should.Throw<ArgumentException>(
            () => new ConnectionOptions { Url = new Uri("/configs", UriKind.Relative) });

    [Fact]
    public void AcceptsAnAbsoluteUrl()
    {
        var url = new Uri("https://proxy.example.com");

        new ConnectionOptions { Url = url }.Url.ShouldBe(url);
    }

    [Fact]
    public void AcceptsNoUrl() => new ConnectionOptions { Url = null }.Url.ShouldBeNull();
}
