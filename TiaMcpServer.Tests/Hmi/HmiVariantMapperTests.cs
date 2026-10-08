using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

/// <summary>One case per row of the spike encoding table (S3) plus the general rules behind it.</summary>
public class HmiVariantMapperTests
{
    private static string Json(HmiVariant v) => v.Value is { } e ? e.GetRawText() : "null";

    [Fact]
    public void NullIsNullTypeAndValue()
    {
        var v = HmiVariantMapper.Map(null);

        Assert.Null(v.Type);
        Assert.Null(v.Value);
    }

    // InitialValue, UpperRange.Value, LowerRange.Value, HmiSubstituteValue.Value, HmiThreshold.Value,
    // HmiLoggingTag.HighLimit/LowLimit, HmiFaceplateInterface.Value: System.String.
    [Fact]
    public void StringRowsAreJsonStrings()
    {
        var empty = HmiVariantMapper.Map("");
        var xml = HmiVariantMapper.Map("<?xml version=\"1.0\"?><body><p>a\"b</p></body>");

        Assert.Equal("System.String", empty.Type);
        Assert.Equal("\"\"", Json(empty));
        Assert.Equal("<?xml version=\"1.0\"?><body><p>a\"b</p></body>", xml.Value!.Value.GetString());
        Assert.Equal(JsonValueKind.String, xml.Value.Value.ValueKind);
    }

    // HmiStartValue, HmiEndValue, PlcStartValue, PlcEndValue: System.Double.
    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(100.0, "100")]
    [InlineData(10.0, "10")]
    [InlineData(-1.5, "-1.5")]
    [InlineData(0.1, "0.1")]
    [InlineData(1e20, "1E+20")]
    // 15 digits do not round-trip, so 17 are written; net10's shortest form would be 0.3333333333333333.
    [InlineData(1.0 / 3, "0.33333333333333331")]
    [InlineData(double.MaxValue, "1.7976931348623157E+308")]
    public void DoubleRowsAreJsonNumbersWrittenRoundTrip(double value, string expected)
    {
        var v = HmiVariantMapper.Map(value);

        Assert.Equal("System.Double", v.Type);
        Assert.Equal(JsonValueKind.Number, v.Value!.Value.ValueKind);
        Assert.Equal(expected, Json(v));
        Assert.Equal(value, v.Value.Value.GetDouble());
    }

    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    public void NotFiniteDoublesAreJsonStrings(double value, string expected)
    {
        var v = HmiVariantMapper.Map(value);

        Assert.Equal("System.Double", v.Type);
        Assert.Equal(JsonValueKind.String, v.Value!.Value.ValueKind);
        Assert.Equal(expected, v.Value.Value.GetString());
    }

    // The encoding is explicit (G7, else G9), so net10 and net48 write the same text.
    [Theory]
    [InlineData(1.5f, "1.5")]
    [InlineData(0.1f, "0.1")]
    [InlineData(1f / 3, "0.333333343")]
    [InlineData(float.MaxValue, "3.40282347E+38")]
    public void FloatRowsAreJsonNumbersWrittenRoundTrip(float value, string expected)
    {
        var v = HmiVariantMapper.Map(value);

        Assert.Equal("System.Single", v.Type);
        Assert.Equal(expected, Json(v));
        Assert.Equal(value, v.Value!.Value.GetSingle());
    }

    // HmiFaceplateInterface.Value: System.Boolean.
    [Theory]
    [InlineData(false, "false")]
    [InlineData(true, "true")]
    public void BooleanRowIsAJsonBoolean(bool value, string expected)
    {
        var v = HmiVariantMapper.Map(value);

        Assert.Equal("System.Boolean", v.Type);
        Assert.Equal(expected, Json(v));
    }

    // HmiAnalogAlarm.ConditionValue is unobserved: the general rule applies (integral, float, decimal).
    [Fact]
    public void IntegralFloatAndDecimalAreJsonNumbers()
    {
        Assert.Equal("-7", Json(HmiVariantMapper.Map((short)-7)));
        Assert.Equal("System.Int16", HmiVariantMapper.Map((short)-7).Type);
        Assert.Equal("4294967295", Json(HmiVariantMapper.Map(uint.MaxValue)));
        Assert.Equal("9223372036854775807", Json(HmiVariantMapper.Map(long.MaxValue)));
        Assert.Equal("255", Json(HmiVariantMapper.Map((byte)255)));
        Assert.Equal("1.5", Json(HmiVariantMapper.Map(1.5f)));
        Assert.Equal("System.Single", HmiVariantMapper.Map(1.5f).Type);
        Assert.Equal("12.30", Json(HmiVariantMapper.Map(12.30m)));
        Assert.Equal("System.Decimal", HmiVariantMapper.Map(12.30m).Type);
    }

    [Fact]
    public void EnumIsItsNameAsString()
    {
        var v = HmiVariantMapper.Map(HmiLimitKind.Constant);

        Assert.Equal(typeof(HmiLimitKind).FullName, v.Type);
        Assert.Equal("\"Constant\"", Json(v));
    }

    [Fact]
    public void OtherTypesAreInvariantToStringAsString()
    {
        var date = HmiVariantMapper.Map(new DateTime(2026, 10, 7, 1, 2, 3, DateTimeKind.Utc));
        var guid = HmiVariantMapper.Map(new Guid("00000000-0000-0000-0000-000000000001"));
        var other = HmiVariantMapper.Map(new Custom());

        Assert.Equal("System.DateTime", date.Type);
        Assert.Equal("10/07/2026 01:02:03", date.Value!.Value.GetString());
        Assert.Equal("00000000-0000-0000-0000-000000000001", guid.Value!.Value.GetString());
        Assert.Equal("custom!", other.Value!.Value.GetString());
    }

    // AlarmBase.AlarmParameterTags: List<string> -> string[]; any other shape -> null plus a message.
    [Fact]
    public void StringListRowKeepsOrderAndEmptyStrings()
    {
        var messages = new List<string>();
        var list = new List<string> { "", "tagB", "", "tagD" };

        var result = HmiVariantMapper.MapStringList(list, messages, "AlarmParameterTags");

        Assert.Equal(new[] { "", "tagB", "", "tagD" }, result);
        Assert.Empty(messages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a string")]
    [InlineData(42)]
    public void StringListOfAnyOtherShapeIsNullWithAMessage(object? value)
    {
        var messages = new List<string>();

        var result = HmiVariantMapper.MapStringList(value, messages, "AlarmParameterTags");

        Assert.Null(result);
        Assert.Contains("AlarmParameterTags", Assert.Single(messages));
    }

    [Fact]
    public void StringListWithNonStringElementsIsNullWithAMessage()
    {
        var messages = new List<string>();

        var result = HmiVariantMapper.MapStringList(new List<object> { "a", 1 }, messages, "AlarmParameterTags");

        Assert.Null(result);
        Assert.Single(messages);
    }

    [Fact]
    public void NeverProducesAnUndefinedJsonElement()
    {
        foreach (var value in new object?[] { null, "", 0.0, true, HmiLimitKind.None, new Custom(), double.NaN })
        {
            var v = HmiVariantMapper.Map(value);
            if (v.Value is { } element)
            {
                Assert.NotEqual(JsonValueKind.Undefined, element.ValueKind);
                JsonSerializer.Serialize(v);
            }
        }
    }

    private enum HmiLimitKind { None, Constant }

    private sealed class Custom
    {
        public override string ToString() => "custom!";
    }
}
