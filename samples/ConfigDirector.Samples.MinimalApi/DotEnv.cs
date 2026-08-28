namespace ConfigDirector.Samples.MinimalApi;

internal static class DotEnv
{
    /// Reads KEY=value pairs into the process environment, so the configuration system's own
    /// environment-variable provider picks them up — including its "__" to ":" section mapping.
    /// Nothing here is a ConfigDirector concept: the file just holds variables a deployment
    /// platform would otherwise export.
    ///
    /// An already-exported variable is left alone, which is the precedence a real deployment
    /// needs: the platform injects the secrets, and the file is a local convenience. An absent
    /// file is not an error.
    internal static void Load(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var entry = line.Trim();
            if (entry.Length == 0 || entry[0] == '#')
            {
                continue;
            }

            var separator = entry.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var name = entry[..separator].Trim();
            if (Environment.GetEnvironmentVariable(name) is not null)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(name, Unquote(entry[(separator + 1)..].Trim()));
        }
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;
}
