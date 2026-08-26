namespace ConfigDirector.Evaluation;

// rapidhash v3.0, "fast" variant. Every SDK must produce the same 64-bit value for the same input,
// or the same user would land in a different percentage bucket depending on which SDK evaluated the
// config. Do not "clean up" the arithmetic here: every mask and shift is load-bearing, and the
// vectors in RapidHashTests are what pin it to the other SDKs.
internal static class RapidHash
{
    private static readonly ulong[] MixingConstants =
    [
        0x2D358DCCAA6C78A5,
        0x8BB84B93962EACC9,
        0x4B33A62ED433D4A3,
        0x4D5A2DA51DE1AA47,
        0xA0761D6478BD642F,
        0xE7037ED1A0B428DB,
        0x90ED1765281C388C,
        0xAAAAAAAAAAAAAAAA,
    ];

    internal static ulong Hash(byte[] data, ulong seed)
    {
        var length = data.Length;
        seed ^= Mix(seed ^ MixingConstants[2], MixingConstants[1]);
        var i = length;

        ulong a;
        ulong b;
        int bi;

        if (length <= 16)
        {
            bi = length;
            if (length >= 4)
            {
                seed ^= (ulong)length;
                if (length >= 8)
                {
                    a = Read64(data, 0);
                    b = Read64(data, length - 8);
                }
                else
                {
                    a = Read32(data, 0);
                    b = Read32(data, length - 4);
                }
            }
            else if (length > 0)
            {
                a = ReadSmall(data, length);
                b = data[length >> 1];
            }
            else
            {
                a = 0;
                b = 0;
            }
        }
        else
        {
            var p = 0;
            if (i > 112)
            {
                var see1 = seed;
                var see2 = seed;
                var see3 = seed;
                var see4 = seed;
                var see5 = seed;
                var see6 = seed;
                do
                {
                    seed = Mix(Read64(data, p) ^ MixingConstants[0], Read64(data, p + 8) ^ seed);
                    see1 = Mix(Read64(data, p + 16) ^ MixingConstants[1], Read64(data, p + 24) ^ see1);
                    see2 = Mix(Read64(data, p + 32) ^ MixingConstants[2], Read64(data, p + 40) ^ see2);
                    see3 = Mix(Read64(data, p + 48) ^ MixingConstants[3], Read64(data, p + 56) ^ see3);
                    see4 = Mix(Read64(data, p + 64) ^ MixingConstants[4], Read64(data, p + 72) ^ see4);
                    see5 = Mix(Read64(data, p + 80) ^ MixingConstants[5], Read64(data, p + 88) ^ see5);
                    see6 = Mix(Read64(data, p + 96) ^ MixingConstants[6], Read64(data, p + 104) ^ see6);
                    p += 112;
                    i -= 112;
                }
                while (i > 112);

                seed ^= see1;
                see2 ^= see3;
                see4 ^= see5;
                seed ^= see6;
                see2 ^= see4;
                seed ^= see2;
            }

            bi = i;
            if (i > 16)
            {
                seed = Mix(Read64(data, p) ^ MixingConstants[2], Read64(data, p + 8) ^ seed);
                if (i > 32)
                {
                    seed = Mix(Read64(data, p + 16) ^ MixingConstants[2], Read64(data, p + 24) ^ seed);
                    if (i > 48)
                    {
                        seed = Mix(Read64(data, p + 32) ^ MixingConstants[1], Read64(data, p + 40) ^ seed);
                        if (i > 64)
                        {
                            seed = Mix(Read64(data, p + 48) ^ MixingConstants[1], Read64(data, p + 56) ^ seed);
                            if (i > 80)
                            {
                                seed = Mix(Read64(data, p + 64) ^ MixingConstants[2], Read64(data, p + 72) ^ seed);
                                if (i > 96)
                                {
                                    seed = Mix(Read64(data, p + 80) ^ MixingConstants[1], Read64(data, p + 88) ^ seed);
                                }
                            }
                        }
                    }
                }
            }

            a = Read64(data, p + i - 16) ^ (ulong)bi;
            b = Read64(data, p + i - 8);
        }

        a ^= MixingConstants[1];
        b ^= seed;
        return Epilogue(a, b, (ulong)bi);
    }

    private static ulong Mix(ulong a, ulong b) => unchecked((a * b) ^ MultiplyHigh(a, b));

    private static ulong Epilogue(ulong a, ulong b, ulong i)
    {
        unchecked
        {
            var x = (a * b) ^ MixingConstants[7];
            var y = MultiplyHigh(a, b) ^ MixingConstants[1] ^ i;
            return (x * y) ^ MultiplyHigh(x, y);
        }
    }

    private static ulong MultiplyHigh(ulong x, ulong y) =>
#if NET8_0_OR_GREATER
        Math.BigMul(x, y, out _);
#else
        MultiplyHighPortable(x, y);
#endif

    // What netstandard2.0 uses, where Math.BigMul does not exist. Compiled on every target so that
    // the suite can check it against the intrinsic: the hash vectors alone would leave this path
    // unexercised whenever the tests run on .NET 8 or later.
    internal static ulong MultiplyHighPortable(ulong x, ulong y)
    {
        unchecked
        {
            ulong xLow = (uint)x;
            var xHigh = x >> 32;
            ulong yLow = (uint)y;
            var yHigh = y >> 32;

            var lowLow = xLow * yLow;
            var lowHigh = xLow * yHigh;
            var highLow = xHigh * yLow;
            var carry = ((lowLow >> 32) + (uint)lowHigh + (uint)highLow) >> 32;

            return (xHigh * yHigh) + (lowHigh >> 32) + (highLow >> 32) + carry;
        }
    }

    // Read little-endian byte by byte rather than through BitConverter, which reads in the running
    // machine's order and would hash differently on a big-endian one.
    private static ulong Read64(byte[] data, int offset) =>
        data[offset]
        | ((ulong)data[offset + 1] << 8)
        | ((ulong)data[offset + 2] << 16)
        | ((ulong)data[offset + 3] << 24)
        | ((ulong)data[offset + 4] << 32)
        | ((ulong)data[offset + 5] << 40)
        | ((ulong)data[offset + 6] << 48)
        | ((ulong)data[offset + 7] << 56);

    private static ulong Read32(byte[] data, int offset) =>
        data[offset]
        | ((ulong)data[offset + 1] << 8)
        | ((ulong)data[offset + 2] << 16)
        | ((ulong)data[offset + 3] << 24);

    private static ulong ReadSmall(byte[] data, int length)
    {
        ulong first = data[0];
        return data[length - 1] | (((first << 5) & 0xFF) << 40) | ((first >> 3) << 48);
    }
}
