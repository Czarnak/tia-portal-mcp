using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Unit tests for the pure coercion helper that maps dynamic Openness attribute values onto the
/// fixed-width DTO numeric members. Only numeric CLR types within the DTO range are accepted;
/// negatives and unrepresentable values are null, never truncated or fabricated.
/// </summary>
public class DynamicNumericAttributeTests
{
    [Theory]
    [InlineData(512)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void CoerceInt32_AcceptsInRangeIntValues(int value)
    {
        Assert.Equal(value, DynamicNumericAttribute.CoerceInt32(value));
    }

    [Theory]
    [InlineData(512)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void CoerceInt32_AcceptsInRangeLongValues(long value)
    {
        var coerced = DynamicNumericAttribute.CoerceInt32(value);
        Assert.NotNull(coerced);
        Assert.Equal(value, coerced.Value);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(0)]
    [InlineData((uint)int.MaxValue)]
    public void CoerceInt32_AcceptsUIntValuesWithinIntRange(uint value)
    {
        Assert.Equal((int)value, DynamicNumericAttribute.CoerceInt32(value));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(0)]
    [InlineData((ulong)int.MaxValue)]
    public void CoerceInt32_AcceptsULongValuesWithinIntRange(ulong value)
    {
        Assert.Equal((int)value, DynamicNumericAttribute.CoerceInt32(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void CoerceInt32_NegativeIntValueIsNull(int value)
    {
        Assert.Null(DynamicNumericAttribute.CoerceInt32(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void CoerceInt32_NegativeLongValueIsNull(long value)
    {
        Assert.Null(DynamicNumericAttribute.CoerceInt32(value));
    }

    [Theory]
    [InlineData((long)int.MaxValue + 1)]
    [InlineData(long.MaxValue)]
    public void CoerceInt32_OutOfIntRangeLongValueIsNull(long value)
    {
        Assert.Null(DynamicNumericAttribute.CoerceInt32(value));
    }

    [Theory]
    [InlineData((ulong)int.MaxValue + 1)]
    [InlineData(ulong.MaxValue)]
    public void CoerceInt32_OutOfIntRangeULongValueIsNull(ulong value)
    {
        Assert.Null(DynamicNumericAttribute.CoerceInt32(value));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(0)]
    [InlineData(uint.MaxValue)]
    public void CoerceUInt32_AcceptsInRangeUIntValues(uint value)
    {
        Assert.Equal(value, DynamicNumericAttribute.CoerceUInt32(value));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(0)]
    [InlineData(uint.MaxValue)]
    public void CoerceUInt32_AcceptsInRangeULongValues(ulong value)
    {
        var coerced = DynamicNumericAttribute.CoerceUInt32(value);
        Assert.NotNull(coerced);
        Assert.Equal(value, coerced.Value);
    }

    [Theory]
    [InlineData((ulong)uint.MaxValue + 1)]
    [InlineData(ulong.MaxValue)]
    public void CoerceUInt32_OutOfUIntRangeULongValueIsNull(ulong value)
    {
        Assert.Null(DynamicNumericAttribute.CoerceUInt32(value));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void CoerceUInt32_AcceptsNonNegativeIntValues(int value)
    {
        Assert.Equal((uint)value, DynamicNumericAttribute.CoerceUInt32(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void CoerceUInt32_NegativeIntValueIsNull(int value)
    {
        Assert.Null(DynamicNumericAttribute.CoerceUInt32(value));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void CoerceUInt32_NegativeLongValueIsNull(long value)
    {
        Assert.Null(DynamicNumericAttribute.CoerceUInt32(value));
    }

    [Theory]
    [InlineData((long)uint.MaxValue + 1)]
    [InlineData(long.MaxValue)]
    public void CoerceUInt32_OutOfUIntRangeLongValueIsNull(long value)
    {
        Assert.Null(DynamicNumericAttribute.CoerceUInt32(value));
    }

    [Fact]
    public void CoerceInt32_NullValueIsNull()
    {
        Assert.Null(DynamicNumericAttribute.CoerceInt32(null));
    }

    [Fact]
    public void CoerceUInt32_NullValueIsNull()
    {
        Assert.Null(DynamicNumericAttribute.CoerceUInt32(null));
    }

    [Theory]
    [InlineData("512")]
    [InlineData(512.0d)]
    [InlineData(true)]
    [InlineData((byte)5)]
    [InlineData((short)5)]
    public void CoerceInt32_NonNumericOrNarrowerClrTypesAreNull(object value)
    {
        Assert.Null(DynamicNumericAttribute.CoerceInt32(value));
    }

    [Theory]
    [InlineData("16")]
    [InlineData(16.0d)]
    [InlineData(false)]
    [InlineData((byte)5)]
    [InlineData((short)5)]
    public void CoerceUInt32_NonNumericOrNarrowerClrTypesAreNull(object value)
    {
        Assert.Null(DynamicNumericAttribute.CoerceUInt32(value));
    }
}
