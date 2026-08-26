using System.Text;
using ConfigDirector.Evaluation;

namespace ConfigDirector.Tests.Evaluation;

public class RapidHashTests
{
    private const ulong Seed = 0x397832987UL;

    // The same vectors the Python and Java SDKs pin their ports with. They cover each short-input
    // length, the 16/17-byte boundary, the 112-byte block loop and its remainder cases, and
    // multi-byte UTF-8.
    [Theory]
    [InlineData("", 5377612543505373799UL)]
    [InlineData("a", 7674800498429868151UL)]
    [InlineData("ab", 12048270741005468339UL)]
    [InlineData("abc", 8205525400821834274UL)]
    [InlineData("abcd", 2559843570930408943UL)]
    [InlineData("abcde", 17182111687207956362UL)]
    [InlineData("abcdefg", 13135472276134024436UL)]
    [InlineData("abcdefgh", 10825195283420988801UL)]
    [InlineData("abcdefghi", 9324307379318471710UL)]
    [InlineData("0123456789abcdef", 8410398172536096822UL)]
    [InlineData("0123456789abcdefg", 8975841632926530338UL)]
    [InlineData("10-11111111-1111-4111-8111-111111111111", 7715065197445012089UL)]
    [InlineData("héllo wörld ✓", 7481766294562949397UL)]
    public void MatchesTheReferenceImplementation(string message, ulong expected) =>
        Hash(message).ShouldBe(expected);

    [Theory]
    [InlineData('x', 48, 1185046273860983588UL)]
    [InlineData('y', 112, 5679430438346846087UL)]
    [InlineData('z', 113, 14103938338420400619UL)]
    [InlineData('w', 240, 15088383192705595115UL)]
    public void MatchesTheReferenceImplementationForBlockSizedInputs(char character, int length, ulong expected) =>
        Hash(new string(character, length)).ShouldBe(expected);

    [Fact]
    public void DependsOnTheSeed()
    {
        Hash("abc", Seed).ShouldNotBe(Hash("abc", Seed + 1));
        Hash("abc", 0).ShouldNotBe(Hash("abc", Seed));
    }

    [Fact]
    public void ReadsTheWholeInput()
    {
        Hash("abcdefgh").ShouldNotBe(Hash("abcdefgi"));
        Hash(new string('x', 240)).ShouldNotBe(Hash(new string('x', 239) + "y"));
    }

    // The vectors above run on .NET 10, which uses Math.BigMul. Without this, the 128-bit multiply
    // netstandard2.0 falls back to would ship untested.
    [Fact]
    public void ThePortableMultiplyHighAgreesWithTheIntrinsic()
    {
        ulong[] edges =
        [
            0, 1, 2, 3, ulong.MaxValue, ulong.MaxValue - 1, uint.MaxValue, (ulong)uint.MaxValue + 1,
            0x8000000000000000, 0x7FFFFFFFFFFFFFFF, 0xFFFFFFFF00000000, 0x00000000FFFFFFFF,
        ];

        foreach (var x in edges)
        {
            foreach (var y in edges)
            {
                RapidHash.MultiplyHighPortable(x, y).ShouldBe(Math.BigMul(x, y, out _), $"{x} * {y}");
            }
        }

        var random = new Random(20260826);
        for (var index = 0; index < 10_000; index++)
        {
            var x = NextUInt64(random);
            var y = NextUInt64(random);

            RapidHash.MultiplyHighPortable(x, y).ShouldBe(Math.BigMul(x, y, out _), $"{x} * {y}");
        }
    }

    private static ulong NextUInt64(Random random)
    {
        var bytes = new byte[8];
        random.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes, 0);
    }

    private static ulong Hash(string message, ulong seed = Seed) =>
        RapidHash.Hash(Encoding.UTF8.GetBytes(message), seed);
}
