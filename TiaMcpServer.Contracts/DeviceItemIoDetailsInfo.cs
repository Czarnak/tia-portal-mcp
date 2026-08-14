using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Structured I/O evidence for one device item, populated only when a <c>read_hardware_config</c>
/// request set <c>includeIoDetails</c> to true. Absent (never null) from a default read so the
/// default response stays byte-identical to earlier versions.
///
/// <para>
/// The members are raw Openness engineering data with three invariants: collections are never null
/// when this object is present; unread scalars stay null (never <c>0</c>/empty-string defaults);
/// and every value is what Openness reported — nothing is fabricated. Degradation notes for
/// unreadable members appear in <see cref="HardwareConfigInfo.Messages"/>.
/// </para>
/// </summary>
public class DeviceItemIoDetailsInfo
{
    /// <summary>
    /// Address objects owned by the device item, in deterministic order
    /// (I/O type, start address, length). Always present when this object is present.
    /// </summary>
    public List<IoAddressInfo> Addresses { get; set; } = new List<IoAddressInfo>();

    /// <summary>
    /// Channels owned by the device item, in deterministic order (number, type). Always present
    /// when this object is present.
    /// </summary>
    public List<IoChannelInfo> Channels { get; set; } = new List<IoChannelInfo>();
}

/// <summary>
/// One Openness <c>Address</c> object: the raw absolute address interval a module exposes.
/// Scalars stay null when Openness could not report them; the interval is never inferred from
/// partial evidence.
/// </summary>
public class IoAddressInfo
{
    /// <summary>
    /// Openness <c>AddressIoType</c> as its enum name: <c>Input</c>, <c>Output</c>,
    /// <c>Substitute</c>, or <c>Diagnosis</c>. Null when unreadable.
    /// </summary>
    public string? IoType { get; set; }

    /// <summary>
    /// Raw absolute start address reported by Openness, in bytes. Null when unreadable.
    /// </summary>
    public int? StartAddress { get; set; }

    /// <summary>
    /// Raw address length reported by Openness, in bytes. Null when unreadable.
    /// </summary>
    public int? Length { get; set; }

    /// <summary>
    /// Dynamic Openness <c>AddressContext</c> where available (e.g. <c>None</c>, <c>Device</c>,
    /// <c>Head</c>). Null when the attribute is not exposed or unreadable.
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// Owning device names reported by the address's controller association, ordinal order,
    /// deduplicated. Empty when the association is absent or unreadable.
    /// </summary>
    public List<string> ControllerNames { get; set; } = new List<string>();
}

/// <summary>
/// One Openness <c>Channel</c> object. <see cref="LogicalAddress"/> is a formatted absolute I/O
/// address derived from <see cref="ChannelAddressBits"/> and <see cref="ChannelWidthBits"/> only
/// when that evidence is present and correctly aligned; otherwise it stays null while the raw
/// bit evidence is preserved untouched.
/// </summary>
public class IoChannelInfo
{
    /// <summary>Channel number as reported by Openness. Null when unreadable.</summary>
    public int? Number { get; set; }

    /// <summary>
    /// Openness <c>ChannelIoType</c> as its enum name: <c>Input</c>, <c>Output</c>, or
    /// <c>Complex</c>. Null when unreadable.
    /// </summary>
    public string? IoType { get; set; }

    /// <summary>
    /// Openness <c>ChannelType</c> as its enum name: <c>Analog</c>, <c>Digital</c>, or
    /// <c>Technology</c>. Null when unreadable.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Raw channel start address reported by Openness, in absolute bits. Null when unreadable.
    /// </summary>
    public int? ChannelAddressBits { get; set; }

    /// <summary>Raw channel width reported by Openness, in bits. Null when unreadable.</summary>
    public uint? ChannelWidthBits { get; set; }

    /// <summary>
    /// Formatted absolute I/O address (e.g. <c>%I4.0</c>, <c>%Q4.0</c>, <c>%IW64</c>,
    /// <c>%QD64</c>) when the I/O type, bit address, and width evidence are all present and
    /// correctly aligned for the width. Null otherwise — <see cref="ChannelAddressBits"/> and
    /// <see cref="ChannelWidthBits"/> stay raw and untouched in that case.
    /// </summary>
    public string? LogicalAddress { get; set; }

    /// <summary>
    /// PLC tags whose normalized absolute I/O interval exactly matches this channel's interval.
    /// Deterministic, ordinal-ordered, and empty when no tag matches or tag matching was not
    /// requested. A tag is never matched across controllers.
    /// </summary>
    public List<IoTagMatchInfo> TagMatches { get; set; } = new List<IoTagMatchInfo>();
}

/// <summary>
/// One PLC tag matched to a channel. Every member is a declared non-null string: the tag table
/// reader always reports a name, data type, and logical address, and a matched tag always has a
/// table and folder path.
/// </summary>
public class IoTagMatchInfo
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    /// <summary>The tag's logical address exactly as reported by the tag table (e.g. <c>%I4.0</c>).</summary>
    public string LogicalAddress { get; set; } = string.Empty;

    public string TableName { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;
}
