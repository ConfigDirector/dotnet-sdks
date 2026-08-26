using System.Text;

namespace ConfigDirector.Evaluation;

internal static class PercentHashing
{
    private const ulong Seed = 0x397832987UL;

    // SEMANTICS.md 7.1. The salt is the identifier first and the config id second; swapping them
    // would hash just as cleanly while putting every user in a different bucket from the other
    // SDKs. Only 1000 values are reachable, 0.0 through 99.9 in tenths.
    internal static double AssignPercentage(string configId, string contextIdentifier) =>
        RapidHash.Hash(Encoding.UTF8.GetBytes(contextIdentifier + "-" + configId), Seed) % 1000 / 10.0;
}
