using System.Reflection;
using System.Reflection.Emit;

namespace ConfigDirector.Tests;

public class SdkIdentityTests
{
    [Fact]
    public void TheServerSdkIdentifiesItselfAsTheDotnetServerSdk() =>
        SdkIdentity.ServerSdk.Name.ShouldBe("dotnet-server-sdk");

    [Fact]
    public void TheServerSdkCarriesTheVersionItWasBuiltAs() =>
        SdkIdentity.ServerSdk.Version.ShouldBe(VersionOf(typeof(SdkIdentity).Assembly));

    [Fact]
    public void AWrapperIsIdentifiedByItsOwnNameAndItsOwnAssemblysVersion()
    {
        var identity = SdkIdentity.For("dotnet-test-wrapper", AssemblyVersioned("2.3.4"));

        identity.Name.ShouldBe("dotnet-test-wrapper");
        identity.Version.ShouldBe("2.3.4");
    }

    [Fact]
    public void TheUserAgentIsTheNameAndVersionSeparatedByASlash() =>
        SdkIdentity.For("dotnet-test-wrapper", AssemblyVersioned("2.3.4")).UserAgent
            .ShouldBe("dotnet-test-wrapper/2.3.4");

    [Fact]
    public void KeepsAPrereleaseSuffixAndDropsTheBuildMetadata() =>
        SdkIdentity.For("dotnet-test-wrapper", AssemblyVersioned("2.3.4-beta.1+9f4c1a")).Version
            .ShouldBe("2.3.4-beta.1");

    [Fact]
    public void SaysSoWhenTheAssemblyDeclaresNoVersion() =>
        SdkIdentity.For("dotnet-test-wrapper", AssemblyVersioned(null)).Version.ShouldBe("0.0.0-dev");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RejectsAMissingName(string? name) =>
        Should.Throw<ArgumentException>(() => SdkIdentity.For(name!, typeof(SdkIdentityTests).Assembly))
            .ParamName.ShouldBe("name");

    [Fact]
    public void RejectsAMissingAssembly() =>
        Should.Throw<ArgumentNullException>(() => SdkIdentity.For("dotnet-test-wrapper", null!))
            .ParamName.ShouldBe("assembly");

    [Fact]
    public void OnlyTheSdkCanBuildAnIdentity() =>
        typeof(SdkIdentity).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .ShouldAllBe(constructor => constructor.IsPrivate);

    private static string VersionOf(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var metadata = informational.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? informational : informational[..metadata];
    }

    private static AssemblyBuilder AssemblyVersioned(string? informationalVersion)
    {
        var name = new AssemblyName("ConfigDirector.Tests.Versioned." + Guid.NewGuid().ToString("N"));
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        if (informationalVersion is not null)
        {
            assembly.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!,
                [informationalVersion]));
        }

        return assembly;
    }
}
