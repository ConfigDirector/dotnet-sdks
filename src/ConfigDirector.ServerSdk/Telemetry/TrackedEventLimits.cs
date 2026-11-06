namespace ConfigDirector.Telemetry;

internal static class TrackedEventLimits
{
    internal const int EventsPerBatch = 1_000;
    internal const int NameMaximumCharacters = 150;
    internal const int DetailsMaximumTopLevelKeys = 20;
    internal const int DetailsMaximumJsonBytes = 1_024;

    internal static bool IsNameWithinTheCharacterLimit(string name)
    {
        var mostUtf16UnitsTheLimitAllows = NameMaximumCharacters * 2;
        if (name.Length == 0 || name.Length > mostUtf16UnitsTheLimitAllows)
        {
            return false;
        }

        var characters = 0;
        for (var index = 0; index < name.Length; index += char.IsSurrogatePair(name, index) ? 2 : 1)
        {
            characters++;
        }

        return characters <= NameMaximumCharacters;
    }
}
