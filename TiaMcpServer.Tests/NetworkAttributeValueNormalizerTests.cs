using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests;

public sealed class NetworkAttributeValueNormalizerTests
{
    private enum SignedEnum : short
    {
        Negative = -7,
        Named = 42,
    }

    private enum UnsignedEnum : ulong
    {
        TooLarge = ulong.MaxValue,
    }

    [Fact]
    public void Normalize_Null_IsRepresentableWithoutAPublicValue()
    {
        var result = NetworkAttributeValueNormalizer.Normalize(null);

        Assert.True(result.IsRepresentable);
        Assert.Null(result.Value);
        Assert.Null(result.ClrTypeName);
    }

    [Theory]
    [InlineData("text", "text")]
    [InlineData('x', "x")]
    public void Normalize_TextValues_PreservesExactText(object input, string expected)
    {
        var result = NetworkAttributeValueNormalizer.Normalize(input);

        Assert.True(result.IsRepresentable);
        Assert.Equal("string", result.Value!.Kind);
        Assert.Equal(expected, result.Value.Value);
    }

    [Fact]
    public void Normalize_Boolean_PreservesBoolean()
    {
        var result = NetworkAttributeValueNormalizer.Normalize(true);

        Assert.True(result.IsRepresentable);
        Assert.Equal("boolean", result.Value!.Kind);
        Assert.IsType<bool>(result.Value.Value);
        Assert.True((bool)result.Value.Value!);
    }

    [Theory]
    [InlineData((sbyte)-1L)]
    [InlineData((byte)2)]
    [InlineData((short)-3)]
    [InlineData((ushort)4)]
    [InlineData(-5)]
    [InlineData((uint)6)]
    [InlineData(-7L)]
    [InlineData((ulong)8)]
    public void Normalize_IntegralValues_UsesAnInt64PublicValue(object input)
    {
        var result = NetworkAttributeValueNormalizer.Normalize(input);

        Assert.True(result.IsRepresentable);
        Assert.Equal("integer", result.Value!.Kind);
        Assert.IsType<long>(result.Value.Value);
        Assert.Equal(Convert.ToInt64(input), result.Value.Value);
    }

    [Theory]
    [InlineData(1.25f)]
    [InlineData(2.5d)]
    public void Normalize_FiniteFloatingPointValues_PreservesOriginalNumericType(object input)
    {
        var result = NetworkAttributeValueNormalizer.Normalize(input);

        Assert.True(result.IsRepresentable);
        Assert.Equal("number", result.Value!.Kind);
        Assert.Equal(input.GetType(), result.Value.Value!.GetType());
        Assert.Equal(input, result.Value.Value);
    }

    [Fact]
    public void Normalize_Decimal_PreservesDecimal()
    {
        var result = NetworkAttributeValueNormalizer.Normalize(123.456m);

        Assert.True(result.IsRepresentable);
        Assert.Equal("number", result.Value!.Kind);
        Assert.IsType<decimal>(result.Value.Value);
        Assert.Equal(123.456m, result.Value.Value);
    }

    [Fact]
    public void Normalize_NamedEnum_PreservesTypeSymbolAndNumericValue()
    {
        var result = NetworkAttributeValueNormalizer.Normalize(SignedEnum.Named);

        var enumValue = Assert.IsType<NetworkEnumValueInfo>(result.Value!.Value);
        Assert.True(result.IsRepresentable);
        Assert.Equal("enum", result.Value.Kind);
        Assert.Equal(typeof(SignedEnum).FullName, enumValue.TypeName);
        Assert.Equal(nameof(SignedEnum.Named), enumValue.Symbol);
        Assert.Equal(42, enumValue.NumericValue);
    }

    [Fact]
    public void Normalize_UnnamedEnum_UsesEmptySymbol()
    {
        var result = NetworkAttributeValueNormalizer.Normalize((SignedEnum)13);

        var enumValue = Assert.IsType<NetworkEnumValueInfo>(result.Value!.Value);
        Assert.Equal(string.Empty, enumValue.Symbol);
        Assert.Equal(13, enumValue.NumericValue);
    }

    [Theory]
    [MemberData(nameof(UnrepresentableValues))]
    public void Normalize_UnrepresentableValues_DoesNotPublishAValue(object input)
    {
        var result = NetworkAttributeValueNormalizer.Normalize(input);

        Assert.False(result.IsRepresentable);
        Assert.Null(result.Value);
        Assert.Equal(input.GetType().FullName, result.ClrTypeName);
    }

    public static IEnumerable<object[]> UnrepresentableValues()
    {
        yield return new object[] { ulong.MaxValue };
        yield return new object[] { float.NaN };
        yield return new object[] { double.PositiveInfinity };
        yield return new object[] { UnsignedEnum.TooLarge };
        yield return new object[] { new[] { 1, 2 } };
        yield return new object[] { DateTime.UnixEpoch };
        yield return new object[] { Guid.NewGuid() };
        yield return new object[] { new ThrowingToString() };
    }

    private sealed class ThrowingToString
    {
        public override string ToString() => throw new InvalidOperationException("must not be called");
    }
}
