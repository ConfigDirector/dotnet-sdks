using System.Security.Cryptography;
using System.Text;

namespace ConfigDirector.Telemetry;

// Identifies a config value by a digest of its rendered form, so a value too large to report
// inline can still be counted. Every SDK has to agree on the identifier for a given value, or one
// value would be counted as two.
internal static class ValueIds
{
    // ceil(128 / log2(62)): the number of base62 digits a 128-bit digest can produce.
    internal const int ValueIdLength = 22;

    private const int DigestBytes = 16;
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    internal static string Generate(string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
#if NET8_0_OR_GREATER
        return ToBase62(SHA256.HashData(utf8));
#else
        using var sha256 = SHA256.Create();
        return ToBase62(sha256.ComputeHash(utf8));
#endif
    }

    private static string ToBase62(byte[] digest)
    {
        var limbs = new uint[DigestBytes / sizeof(uint)];
        for (var index = 0; index < limbs.Length; index++)
        {
            var offset = index * sizeof(uint);
            limbs[index] = ((uint)digest[offset] << 24)
                | ((uint)digest[offset + 1] << 16)
                | ((uint)digest[offset + 2] << 8)
                | digest[offset + 3];
        }

        // Always writing the full width is what pads a short identifier with leading zeros. The
        // base62 packages encode a leading zero byte as a digit of its own instead, which would
        // produce identifiers no other SDK agrees with.
        var digits = new char[ValueIdLength];
        for (var position = ValueIdLength - 1; position >= 0; position--)
        {
            ulong remainder = 0;
            for (var index = 0; index < limbs.Length; index++)
            {
                var accumulated = (remainder << 32) | limbs[index];
                limbs[index] = (uint)(accumulated / 62);
                remainder = accumulated % 62;
            }

            digits[position] = Base62[(int)remainder];
        }

        return new string(digits);
    }
}
