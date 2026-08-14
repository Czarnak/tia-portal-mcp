using System;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts;

/// <summary>
/// A closed half-open interval of absolute bit positions, the normalized form of every Siemens
/// absolute I/O address this repository understands. A bit address <c>%I4.0</c> is
/// <c>StartBit = 32</c>; a word <c>%IW64</c> is <c>StartBit = 512, BitCount = 16</c>.
/// </summary>
public readonly record struct IoAbsoluteBitInterval(int StartBit, uint BitCount)
{
    public int EndBitExclusive => StartBit + (int)BitCount;
}

/// <summary>
/// A normalized absolute I/O address: an I/O area (<c>I</c> for input, <c>Q</c> for output) plus
/// an absolute bit interval. This is the exact identity the tag matcher compares — the same
/// numeric address in a different area is a different identity.
/// </summary>
public readonly record struct IoAbsoluteIoAddress(string Area, IoAbsoluteBitInterval Interval);

/// <summary>
/// Pure, TIA-free parsing and formatting of Siemens absolute I/O addresses.
///
/// <para>
/// Supported spellings: bit (<c>%I4.0</c>/<c>%Q4.0</c>), byte (<c>%IB4</c>/<c>%QB4</c>), word
/// (<c>%IW64</c>/<c>%QW64</c>), and double word (<c>%ID64</c>/<c>%QD64</c>). Harmless casing and
/// surrounding whitespace are normalized. <c>%M</c> memory, DB addresses, and symbolic-only
/// addresses are rejected.
/// </para>
///
/// <para>
/// Formatting is conservative: a logical address is emitted only when the I/O area, absolute bit
/// start, and width evidence are all present and correctly aligned for that width. Word intervals
/// must start on an even byte; double-word intervals on a byte divisible by four; byte intervals
/// on a byte boundary; bit intervals on any bit. Any other combination yields null while the raw
/// evidence is preserved untouched by the caller.
/// </para>
/// </summary>
public static class IoLogicalAddressFormatter
{
    public const string InputArea = "I";
    public const string OutputArea = "Q";

    private const string BitPattern = @"^%(?<area>[IQ])(?<byte>\d+)\.(?<bit>\d+)$";
    private const string BytePattern = @"^%(?<area>[IQ])B(?<byte>\d+)$";
    private const string WordPattern = @"^%(?<area>[IQ])W(?<byte>\d+)$";
    private const string DWordPattern = @"^%(?<area>[IQ])D(?<byte>\d+)$";

    private static readonly Regex BitRegex = new(BitPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ByteRegex = new(BytePattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WordRegex = new(WordPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DWordRegex = new(DWordPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Normalizes an Openness I/O-type enum name to this repository's I/O area vocabulary:
    /// <c>Input</c> → <c>I</c>, <c>Output</c> → <c>Q</c> (harmless casing and surrounding
    /// whitespace are ignored). Anything else (including <c>Complex</c> channels, which have no
    /// absolute I/O address) maps to null.
    /// </summary>
    public static string? NormalizeArea(string? opennessIoType)
    {
        if (string.IsNullOrWhiteSpace(opennessIoType))
        {
            return null;
        }

        return opennessIoType!.Trim() switch
        {
            var value when string.Equals(value, "Input", StringComparison.OrdinalIgnoreCase) => InputArea,
            var value when string.Equals(value, "Output", StringComparison.OrdinalIgnoreCase) => OutputArea,
            _ => null,
        };
    }

    /// <summary>
    /// Parses any supported absolute I/O spelling into its normalized area + bit interval.
    /// Returns false — with no fabricated identity — for <c>%M</c>, DB, symbolic, misaligned, or
    /// unrecognized text.
    /// </summary>
    public static bool TryParse(string? text, out IoAbsoluteIoAddress? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text!.Trim().ToUpperInvariant();

        var bitMatch = BitRegex.Match(normalized);
        if (bitMatch.Success)
        {
            var byteNumber = int.Parse(bitMatch.Groups["byte"].Value);
            var bitNumber = int.Parse(bitMatch.Groups["bit"].Value);
            if (bitNumber is < 0 or > 7)
            {
                return false;
            }

            address = new IoAbsoluteIoAddress(
                bitMatch.Groups["area"].Value,
                new IoAbsoluteBitInterval(byteNumber * 8 + bitNumber, 1));
            return true;
        }

        var byteMatch = ByteRegex.Match(normalized);
        if (byteMatch.Success)
        {
            var byteNumber = int.Parse(byteMatch.Groups["byte"].Value);
            address = new IoAbsoluteIoAddress(
                byteMatch.Groups["area"].Value,
                new IoAbsoluteBitInterval(byteNumber * 8, 8));
            return true;
        }

        var wordMatch = WordRegex.Match(normalized);
        if (wordMatch.Success)
        {
            var byteNumber = int.Parse(wordMatch.Groups["byte"].Value);
            if (byteNumber % 2 != 0)
            {
                return false;
            }

            address = new IoAbsoluteIoAddress(
                wordMatch.Groups["area"].Value,
                new IoAbsoluteBitInterval(byteNumber * 8, 16));
            return true;
        }

        var dwordMatch = DWordRegex.Match(normalized);
        if (dwordMatch.Success)
        {
            var byteNumber = int.Parse(dwordMatch.Groups["byte"].Value);
            if (byteNumber % 4 != 0)
            {
                return false;
            }

            address = new IoAbsoluteIoAddress(
                dwordMatch.Groups["area"].Value,
                new IoAbsoluteBitInterval(byteNumber * 8, 32));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Formats a channel's I/O evidence into a logical address. All three inputs must be present
    /// (<paramref name="ioType"/> normalizable to <c>I</c>/<c>Q</c>, a non-negative
    /// <paramref name="startBit"/>, and a supported <paramref name="widthBits"/>), and the start
    /// must be aligned for the width. Returns null otherwise — never a partially derived guess.
    /// </summary>
    public static string? FormatLogicalAddress(string? ioType, int? startBit, uint? widthBits)
    {
        var area = NormalizeArea(ioType);
        if (area is null || startBit is null || widthBits is null || startBit < 0)
        {
            return null;
        }

        var byteNumber = startBit.Value / 8;
        var bitNumber = startBit.Value % 8;

        switch (widthBits.Value)
        {
            case 1:
                return $"%{area}{byteNumber}.{bitNumber}";
            case 8:
                return bitNumber == 0 ? $"%{area}B{byteNumber}" : null;
            case 16:
                return bitNumber == 0 && byteNumber % 2 == 0 ? $"%{area}W{byteNumber}" : null;
            case 32:
                return bitNumber == 0 && byteNumber % 4 == 0 ? $"%{area}D{byteNumber}" : null;
            default:
                return null;
        }
    }
}
