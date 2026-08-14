using System;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Pure, TIA-free deterministic matching of a PLC tag's normalized absolute I/O interval against a
/// physical channel's interval.
///
/// <para>
/// Conservative by design: a tag matches a channel only when both the I/O area (<c>I</c>/<c>Q</c>)
/// and the absolute bit interval are identical. There is no overlap matching, no containment
/// matching, and no first-match fallback — a tag either names exactly the same interval as the
/// channel or it is not a match. Several tags may match one channel (they all name the same
/// interval); a single tag never matches two channels with different intervals.
/// </para>
/// </summary>
public static class IoTagMatcher
{
    /// <summary>
    /// Returns true when a parsed tag address names exactly the same I/O area and absolute bit
    /// interval as the channel's evidence. Null channel evidence (unreadable I/O type, start, or
    /// width) never matches.
    /// </summary>
    public static bool MatchesChannel(
        IoAbsoluteIoAddress tag,
        string? channelIoType,
        int? channelStartBit,
        uint? channelWidthBits)
    {
        var channelArea = IoLogicalAddressFormatter.NormalizeArea(channelIoType);
        if (channelArea is null
            || !string.Equals(tag.Area, channelArea, StringComparison.Ordinal)
            || channelStartBit is null
            || channelWidthBits is null
            || channelStartBit < 0)
        {
            return false;
        }

        return tag.Interval == new IoAbsoluteBitInterval(channelStartBit.Value, channelWidthBits.Value);
    }
}
