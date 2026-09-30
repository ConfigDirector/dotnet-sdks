using System.Reflection;

namespace ConfigDirector.Testing;

internal static class SdkVersionCheck
{
    private const string DevelopmentVersion = "0.0.0-dev";

    internal static string? VersionOf(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    internal static void Verify(string? sdkVersion, string? testingVersion)
    {
        if (string.IsNullOrEmpty(sdkVersion)
            || string.IsNullOrEmpty(testingVersion)
            || IsDevelopmentBuild(sdkVersion!)
            || IsDevelopmentBuild(testingVersion!)
            || WithoutMetadata(sdkVersion!) == WithoutMetadata(testingVersion!))
        {
            return;
        }

        throw new InvalidOperationException(
            $"ConfigDirector.ServerSdk.Testing {WithoutMetadata(testingVersion!)} requires ConfigDirector.ServerSdk "
                + $"{WithoutMetadata(testingVersion!)}, but ConfigDirector.ServerSdk {WithoutMetadata(sdkVersion!)} is "
                + "loaded. The testing package relies on the SDK's internals, so both must be the same version: "
                + "reference the same version of both packages, and check for a newer ConfigDirector.ServerSdk pulled "
                + "in by another dependency.");
    }

    private static bool IsDevelopmentBuild(string version) =>
        string.Equals(WithoutMetadata(version), DevelopmentVersion, StringComparison.Ordinal);

    private static string WithoutMetadata(string version)
    {
        var metadata = version.IndexOf('+');
        return metadata < 0 ? version : version.Substring(0, metadata);
    }
}
