using System.Globalization;

namespace ConfigDirector;

// ConfigDirector spells a config's type in lowercase, and every SDK has to agree on that spelling.
internal static class ConfigTypes
{
    private static readonly Dictionary<string, ConfigType> ByWireName =
        Values().ToDictionary(WireName, StringComparer.OrdinalIgnoreCase);

    // The generic overload is the one AOT can honour; the non-generic has to build the array at
    // runtime, which a trimmed application may not be able to do.
    private static ConfigType[] Values() =>
#if NET8_0_OR_GREATER
        Enum.GetValues<ConfigType>();
#else
        ((ConfigType[])Enum.GetValues(typeof(ConfigType)));
#endif

    internal static string WireName(ConfigType type) =>
        type.ToString().ToLower(CultureInfo.InvariantCulture);

    // A type added to ConfigDirector after this SDK was released is not an error: it is simply not
    // one this version knows, and an evaluation must still work.
    internal static ConfigType? FromWireName(string? name) =>
        name is not null && ByWireName.TryGetValue(name, out var type) ? type : null;
}
