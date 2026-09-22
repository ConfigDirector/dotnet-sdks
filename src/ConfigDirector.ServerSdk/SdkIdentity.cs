using System.Reflection;

namespace ConfigDirector;

// Identifies the SDK, or the wrapper built on it, to the server. The version is read from the
// assembly so it cannot drift from what was actually published; a development build says so.
internal sealed class SdkIdentity
{
    private const string DevelopmentVersion = "0.0.0-dev";

    private SdkIdentity(string name, string version)
    {
        Name = name;
        Version = version;
        UserAgent = name + "/" + version;
    }

    internal static SdkIdentity ServerSdk { get; } = For("dotnet-server-sdk", typeof(SdkIdentity).Assembly);

    internal string Name { get; }

    internal string Version { get; }

    internal string UserAgent { get; }

    internal static SdkIdentity For(string name, Assembly assembly)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("The SDK identity needs a name.", nameof(name));
        }

        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        return new SdkIdentity(name, ReadVersion(assembly));
    }

    private static string ReadVersion(Assembly assembly)
    {
        var informational = assembly
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
