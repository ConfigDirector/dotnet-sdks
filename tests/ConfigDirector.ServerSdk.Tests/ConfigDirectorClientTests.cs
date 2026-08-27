namespace ConfigDirector.Tests;

// What a client rejects before it ever connects. Everything that needs a server is an integration
// test, driven through the public API against a stubbed ConfigDirector.
public class ConfigDirectorClientTests
{
    [Fact]
    public void RejectsAMissingServerSdkKey() =>
        Should.Throw<ArgumentNullException>(() => new ConfigDirectorClient(null!));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsABlankServerSdkKey(string key) =>
        Should.Throw<ArgumentException>(() => new ConfigDirectorClient(key));
}
