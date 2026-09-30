namespace ConfigDirector.Testing.Tests;

public sealed class SdkVersionCheckTests
{
    [Fact]
    public void PassesWhenBothVersionsMatch() =>
        Should.NotThrow(() => SdkVersionCheck.Verify("1.6.0", "1.6.0"));

    [Fact]
    public void IgnoresBuildMetadata() =>
        Should.NotThrow(() => SdkVersionCheck.Verify("1.6.0+9f4c1a", "1.6.0+0b2d7e"));

    [Fact]
    public void ThrowsNamingBothVersionsWhenTheyDiffer()
    {
        var failure = Should.Throw<InvalidOperationException>(() => SdkVersionCheck.Verify("1.7.0+9f4c1a", "1.6.0"));

        failure.Message.ShouldContain("ConfigDirector.ServerSdk.Testing 1.6.0");
        failure.Message.ShouldContain("ConfigDirector.ServerSdk 1.7.0");
    }

    [Fact]
    public void SkipsWhenEitherVersionIsUnavailable()
    {
        Should.NotThrow(() => SdkVersionCheck.Verify(null, "1.6.0"));
        Should.NotThrow(() => SdkVersionCheck.Verify("1.7.0", null));
        Should.NotThrow(() => SdkVersionCheck.Verify("", "1.6.0"));
    }

    [Fact]
    public void SkipsTheDevelopmentPlaceholder()
    {
        Should.NotThrow(() => SdkVersionCheck.Verify("0.0.0-dev", "1.6.0"));
        Should.NotThrow(() => SdkVersionCheck.Verify("1.7.0", "0.0.0-dev+9f4c1a"));
    }

    [Fact]
    public void ReadsTheSameVersionFromBothAssembliesOfThisBuild()
    {
        var sdkVersion = SdkVersionCheck.VersionOf(typeof(IConfigDirectorClient).Assembly);
        var testingVersion = SdkVersionCheck.VersionOf(typeof(ConfigDirectorTesting).Assembly);

        sdkVersion.ShouldNotBeNullOrEmpty();
        sdkVersion.ShouldBe(testingVersion);
    }
}
