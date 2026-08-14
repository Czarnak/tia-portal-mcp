using System;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Coerces dynamic Openness attribute values to the fixed-width DTO numeric members. Openness can
/// report the same attribute as a 32-bit or a 64-bit integer (for example <c>ChannelAddress</c> as
/// <see cref="int"/> or <see cref="long"/> and <c>ChannelWidth</c> as <see cref="uint"/> or
/// <see cref="ulong"/>) depending on the V21 release. Only the 32-bit and 64-bit integer widths
/// Openness reports for these attributes (int, long, uint, and ulong) are accepted; everything
/// else — including narrower numeric types, strings, and booleans — is null. Every conversion is
/// range-guarded, and a value that does not fit its DTO member — including any negative value — is
/// null rather than truncated. A value is never fabricated from another type,
/// and no text or reflection is involved.
/// </summary>
public static class DynamicNumericAttribute
{
    /// <summary>Coerces a dynamic attribute value to the DTO's <see cref="int"/> member.</summary>
    public static int? CoerceInt32(object? value)
    {
        switch (value)
        {
            case int intValue when intValue >= 0:
                return intValue;
            case long longValue when longValue >= 0 && longValue <= int.MaxValue:
                return (int)longValue;
            case uint uintValue when uintValue <= int.MaxValue:
                return (int)uintValue;
            case ulong ulongValue when ulongValue <= int.MaxValue:
                return (int)ulongValue;
            default:
                return null;
        }
    }

    /// <summary>Coerces a dynamic attribute value to the DTO's <see cref="uint"/> member.</summary>
    public static uint? CoerceUInt32(object? value)
    {
        switch (value)
        {
            case uint uintValue:
                return uintValue;
            case ulong ulongValue when ulongValue <= uint.MaxValue:
                return (uint)ulongValue;
            case int intValue when intValue >= 0:
                return (uint)intValue;
            case long longValue when longValue >= 0 && longValue <= uint.MaxValue:
                return (uint)longValue;
            default:
                return null;
        }
    }
}
