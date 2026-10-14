using System.Text;

namespace ConfigDirector.Evaluation;

internal static class PercentHashing
{
    private const ulong Seed = 0x397832987UL;

    internal const double PercentageWithoutIdentifier = 0.0;

    // SEMANTICS.md 7.1. The salt is the identifier first and the config id second, followed by a
    // colon and the kind for any kind but user; swapping them would hash just as cleanly while
    // putting every context in a different bucket from the other SDKs. Only 1000 values are reachable, 0.0 through 99.9 in tenths.
    internal static double AssignPercentage(string configId, string contextIdentifier, string kind = ContextKinds.User) =>
        RapidHash.Hash(Encoding.UTF8.GetBytes(HashInput(configId, contextIdentifier, kind)), Seed) % 1000 / 10.0;

    private static string HashInput(string configId, string contextIdentifier, string kind) =>
        kind == ContextKinds.User
            ? contextIdentifier + "-" + configId
            : contextIdentifier + "-" + configId + ":" + kind;
}
