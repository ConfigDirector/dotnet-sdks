using System.Reflection;

namespace ConfigDirector;

// Identifies this SDK to the server. The version is read from the assembly so it cannot drift from
// what was actually published; a development build says so.
internal static class SdkIdentity
{
    internal const string Name = "dotnet-server-sdk";

    private const string DevelopmentVersion = "0.0.0-dev";

    internal static string Version { get; } = ReadVersion();

    internal static string UserAgent { get; } = Name + "/" + Version;

    private static string ReadVersion()
    {
        var informational = typeof(SdkIdentity).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
        {
            return DevelopmentVersion;
        }

        // Source-link builds append "+<commit>", which is not part of the version.
        var metadata = informational!.IndexOf('+');
        return metadata < 0 ? informational : informational.Substring(0, metadata);
    }
}
