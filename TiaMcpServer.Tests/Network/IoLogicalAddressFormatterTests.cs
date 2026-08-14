using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Pure, TIA-free tests for <see cref="IoLogicalAddressFormatter"/>: parsing every supported
/// Siemens absolute I/O spelling into a normalized bit interval, rejecting everything else, and
/// formatting a logical address only when the evidence is present and aligned.
/// </summary>
public class IoLogicalAddressFormatterTests
{
    // ---------------------------------------------------------------------
    // Parsing
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("%I4.0", "I", 32, 1u)]
    [InlineData("%Q4.0", "Q", 32, 1u)]
    [InlineData("%IB4", "I", 32, 8u)]
    [InlineData("%QB4", "Q", 32, 8u)]
    [InlineData("%IW64", "I", 512, 16u)]
    [InlineData("%QW64", "Q", 512, 16u)]
    [InlineData("%ID64", "I", 512, 32u)]
    [InlineData("%QD64", "Q", 512, 32u)]
    public void TryParse_AcceptsEverySupportedAbsoluteIoSpelling(
        string text,
        string expectedArea,
        int expectedStartBit,
        uint expectedBitCount)
    {
        Assert.True(IoLogicalAddressFormatter.TryParse(text, out var address));
        Assert.NotNull(address);
        Assert.Equal(expectedArea, address!.Value.Area);
        Assert.Equal(expectedStartBit, address.Value.Interval.StartBit);
        Assert.Equal(expectedBitCount, address.Value.Interval.BitCount);
    }

    [Theory]
    [InlineData("  %i4.0  ")]
    [InlineData("%i4.0")]
    [InlineData(" %Q4.0 ")]
    [InlineData("%ib4")]
    [InlineData("%qw64")]
    [InlineData("%id64")]
    public void TryParse_NormalizesHarmlessCasingAndSurroundingWhitespace(string text)
    {
        Assert.True(IoLogicalAddressFormatter.TryParse(text, out var address));
        Assert.NotNull(address);
    }

    [Theory]
    [InlineData("%M4.0")]
    [InlineData("%MW64")]
    [InlineData("%DB1.DBX0.0")]
    [InlineData("DB1.DBX0.0")]
    [InlineData("Motor.Run")]
    [InlineData("I4.0")]
    [InlineData("4.0")]
    [InlineData("%I")]
    [InlineData("%I4")]
    [InlineData("%I4.8")]
    [InlineData("%I4.-1")]
    [InlineData("%IW63")]
    [InlineData("%ID62")]
    [InlineData("%IX4")]
    [InlineData("%IW64.0")]
    [InlineData("%I4,0")]
    [InlineData("%")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsMemoryDbSymbolicMisalignedAndUnrecognizedText(string? text)
    {
        Assert.False(IoLogicalAddressFormatter.TryParse(text, out var address));
        Assert.Null(address);
    }

    [Fact]
    public void TryParse_RejectsABitNumberOutsideZeroToSeven()
    {
        Assert.False(IoLogicalAddressFormatter.TryParse("%I4.8", out _));
        Assert.False(IoLogicalAddressFormatter.TryParse("%Q4.9", out _));
    }

    [Theory]
    [InlineData("%I9999999999999999999999999999999999999999999999999999999999999999.0")]
    [InlineData("%IB9999999999999999999999999999999999999999999999999999999999999999")]
    [InlineData("%IW9999999999999999999999999999999999999999999999999999999999999999")]
    [InlineData("%ID9999999999999999999999999999999999999999999999999999999999999999")]
    [InlineData("%I0.9999999999999999999999999999999999999999999999999999999999999999")]
    public void TryParse_ExtremelyLongDigitSequences_ReturnsFalseWithoutThrowing(string text)
    {
        Assert.False(IoLogicalAddressFormatter.TryParse(text, out var address));
        Assert.Null(address);
    }

    [Theory]
    [InlineData("%I2147483647.0")]
    [InlineData("%IB2147483647")]
    [InlineData("%IW2147483646")]
    [InlineData("%ID2147483644")]
    [InlineData("%I1000000000.0")]
    [InlineData("%IB1000000000")]
    [InlineData("%IW1000000000")]
    [InlineData("%ID1000000000")]
    public void TryParse_OversizedByteValues_ReturnsFalseWithoutThrowing(string text)
    {
        Assert.False(IoLogicalAddressFormatter.TryParse(text, out var address));
        Assert.Null(address);
    }

    [Fact]
    public void TryParse_BoundariesAroundIntMaxValueDiv8_AcceptsMaxRepresentableBitAndRejectsOverflow()
    {
        // 268435455 * 8 + 7 = 2147483647 (int.MaxValue)
        Assert.True(IoLogicalAddressFormatter.TryParse("%I268435455.7", out var maxBit));
        Assert.NotNull(maxBit);
        Assert.Equal(int.MaxValue, maxBit!.Value.Interval.StartBit);
        Assert.Equal(1u, maxBit.Value.Interval.BitCount);

        // 268435456 * 8 = 2147483648 > int.MaxValue
        Assert.False(IoLogicalAddressFormatter.TryParse("%I268435456.0", out var overflowBit));
        Assert.Null(overflowBit);

        // Byte: (int.MaxValue - 8) / 8 = 268435454 -> startBit = 2147483632
        Assert.True(IoLogicalAddressFormatter.TryParse("%IB268435454", out var maxByte));
        Assert.NotNull(maxByte);
        Assert.Equal(2147483632, maxByte!.Value.Interval.StartBit);
        Assert.Equal(8u, maxByte.Value.Interval.BitCount);
        Assert.False(IoLogicalAddressFormatter.TryParse("%IB268435455", out _));

        // Word: (int.MaxValue - 16) / 8 = 268435453 (even: 268435452) -> startBit = 2147483616
        Assert.True(IoLogicalAddressFormatter.TryParse("%IW268435452", out var maxWord));
        Assert.NotNull(maxWord);
        Assert.Equal(2147483616, maxWord!.Value.Interval.StartBit);
        Assert.Equal(16u, maxWord.Value.Interval.BitCount);
        Assert.False(IoLogicalAddressFormatter.TryParse("%IW268435454", out _));

        // DWord: (int.MaxValue - 32) / 8 = 268435451 (divisible by 4: 268435448) -> startBit = 2147483584
        Assert.True(IoLogicalAddressFormatter.TryParse("%ID268435448", out var maxDWord));
        Assert.NotNull(maxDWord);
        Assert.Equal(2147483584, maxDWord!.Value.Interval.StartBit);
        Assert.Equal(32u, maxDWord.Value.Interval.BitCount);
        Assert.False(IoLogicalAddressFormatter.TryParse("%ID268435452", out _));
    }

    // ---------------------------------------------------------------------
    // Area normalization
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("Input", "I")]
    [InlineData("Output", "Q")]
    [InlineData("input", "I")]
    [InlineData(" output ", "Q")]
    [InlineData("Complex", null)]
    [InlineData("None", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("Unrecognized", null)]
    public void NormalizeArea_MapsOpennessIoTypesToAreaVocabulary(string? opennessIoType, string? expected)
    {
        Assert.Equal(expected, IoLogicalAddressFormatter.NormalizeArea(opennessIoType));
    }

    // ---------------------------------------------------------------------
    // Formatting
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("Input", 32, 1u, "%I4.0")]
    [InlineData("Output", 32, 1u, "%Q4.0")]
    [InlineData("Input", 32, 8u, "%IB4")]
    [InlineData("Output", 32, 8u, "%QB4")]
    [InlineData("Input", 512, 16u, "%IW64")]
    [InlineData("Output", 512, 16u, "%QW64")]
    [InlineData("Input", 512, 32u, "%ID64")]
    [InlineData("Output", 512, 32u, "%QD64")]
    public void FormatLogicalAddress_EmitsEverySupportedWidth(
        string ioType,
        int startBit,
        uint widthBits,
        string expected)
    {
        Assert.Equal(expected, IoLogicalAddressFormatter.FormatLogicalAddress(ioType, startBit, widthBits));
    }

    [Theory]
    [InlineData("Input", 33, 1u, "%I4.1")]
    [InlineData("Output", 39, 1u, "%Q4.7")]
    [InlineData("Input", 40, 8u, "%IB5")]
    [InlineData("Output", 528, 16u, "%QW66")]
    [InlineData("Input", 544, 32u, "%ID68")]
    public void FormatLogicalAddress_ConvertsBitAddressesToByteAndBitSpellings(
        string ioType,
        int startBit,
        uint widthBits,
        string expected)
    {
        Assert.Equal(expected, IoLogicalAddressFormatter.FormatLogicalAddress(ioType, startBit, widthBits));
    }

    [Theory]
    [InlineData("Input", 33, 8u)]
    [InlineData("Input", 33, 16u)]
    [InlineData("Input", 520, 16u)]
    [InlineData("Input", 520, 32u)]
    public void FormatLogicalAddress_RejectsMisalignedByteWordAndDwordStarts(
        string ioType,
        int startBit,
        uint widthBits)
    {
        // 33 is not a byte boundary; byte 65 is odd (word) and not divisible by 4 (dword).
        Assert.Null(IoLogicalAddressFormatter.FormatLogicalAddress(ioType, startBit, widthBits));
    }

    [Theory]
    [InlineData("Input", 32, 4u)]
    [InlineData("Input", 32, 64u)]
    [InlineData("Input", 32, 0u)]
    public void FormatLogicalAddress_RejectsUnsupportedWidths(string ioType, int startBit, uint widthBits)
    {
        Assert.Null(IoLogicalAddressFormatter.FormatLogicalAddress(ioType, startBit, widthBits));
    }

    [Theory]
    [InlineData(null, 32, 1u)]
    [InlineData("Complex", 32, 1u)]
    [InlineData("Input", null, 1u)]
    [InlineData("Input", 32, null)]
    [InlineData("Input", -1, 1u)]
    [InlineData("Unrecognized", 32, 1u)]
    public void FormatLogicalAddress_NeverFormatsFromPartialOrInvalidEvidence(
        string? ioType,
        int? startBit,
        uint? widthBits)
    {
        Assert.Null(IoLogicalAddressFormatter.FormatLogicalAddress(ioType, startBit, widthBits));
    }

    [Fact]
    public void FormatThenParse_RoundTripsEverySupportedWidth()
    {
        foreach (var (ioType, startBit, widthBits) in new[]
        {
            ("Input", 32, 1u),
            ("Output", 39, 1u),
            ("Input", 40, 8u),
            ("Output", 528, 16u),
            ("Input", 544, 32u),
        })
        {
            var formatted = IoLogicalAddressFormatter.FormatLogicalAddress(ioType, startBit, widthBits);
            Assert.NotNull(formatted);
            Assert.True(IoLogicalAddressFormatter.TryParse(formatted, out var parsed));
            Assert.Equal(startBit, parsed!.Value.Interval.StartBit);
            Assert.Equal(widthBits, parsed.Value.Interval.BitCount);
        }
    }
}
