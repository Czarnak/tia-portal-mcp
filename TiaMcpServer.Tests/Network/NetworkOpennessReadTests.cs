using Siemens.Engineering;
using TiaMcpServer.OpennessWorker.Openness.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkOpennessReadTests
{
    [Fact]
    public void OptionalString_PortalLossPropagatesWithoutADegradationMessage()
    {
        var failure = new NonRecoverableException("Portal lost");
        var messages = new List<string>();

        var thrown = Assert.Throws<NonRecoverableException>(() =>
            NetworkOpennessRead.ReadOptionalString(() => throw failure, "device name", messages));

        Assert.Same(failure, thrown);
        Assert.Empty(messages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Device_A")]
    public void OptionalString_PreservesSuccessfullyReadValues(string? value)
    {
        var messages = new List<string>();

        Assert.Equal(value, NetworkOpennessRead.ReadOptionalString(() => value, "device name", messages));
        Assert.Empty(messages);
    }

    [Fact]
    public void OptionalString_UnsupportedPropertyReportsOneMessage()
    {
        var messages = new List<string>();

        Assert.Null(NetworkOpennessRead.ReadOptionalString(
            () => throw new EngineeringNotSupportedException("unavailable"), "device name", messages));

        Assert.Equal(new[] { "Could not read device name: unavailable" }, messages);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("bool")]
    [InlineData("int")]
    [InlineData("nonNegativeInt")]
    [InlineData("enum")]
    [InlineData("attribute")]
    [InlineData("identityString")]
    [InlineData("identityInt")]
    [InlineData("identityAttribute")]
    [InlineData("tryAttribute")]
    [InlineData("tryEnum")]
    public void SharedReads_PropagatePortalLossAndProgrammingFailures(string kind)
    {
        var messages = new List<string>();
        var portalLoss = new NonRecoverableException("Portal lost");
        var programmingFailure = new InvalidOperationException("unexpected state");

        Assert.Same(portalLoss, Assert.Throws<NonRecoverableException>(() => ReadFailure(kind, portalLoss, messages)));
        Assert.Same(programmingFailure, Assert.Throws<InvalidOperationException>(() => ReadFailure(kind, programmingFailure, messages)));
        Assert.Empty(messages);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("bool")]
    [InlineData("int")]
    [InlineData("nonNegativeInt")]
    [InlineData("enum")]
    [InlineData("attribute")]
    public void OptionalReads_EngineeringFailureIsNullWithOneDiagnostic(string kind)
    {
        var messages = new List<string>();

        Assert.Null(ReadFailure(kind, new EngineeringNotSupportedException("unavailable"), messages));

        Assert.Equal(new[] { "Could not read field: unavailable" }, messages);
    }

    [Fact]
    public void IdentityReads_UnreadableValuesCannotBecomeSelectors()
    {
        var failure = new EngineeringException("unavailable");
        var text = NetworkOpennessRead.ReadTypedIdentityString(() => throw failure, "Name");
        var number = NetworkOpennessRead.ReadTypedIdentityInt(() => throw failure, "Number");
        var attribute = NetworkOpennessRead.ReadExactStringIdentityAttribute(
            new AttributeObject(() => throw failure), "SubnetId", "Subnet identity");

        Assert.False(text.IsUsable);
        Assert.False(number.IsUsable);
        Assert.False(attribute.IsUsable);
        Assert.Equal("readFailed", text.SnapshotToken);
        Assert.Equal("readFailed", number.SnapshotToken);
        Assert.Equal("readFailed", attribute.SnapshotToken);
    }

    [Theory]
    [InlineData(null, "null")]
    [InlineData("", "blank")]
    [InlineData(" ", "blank")]
    public void IdentityString_NullOrBlankCannotBecomeASelector(string? value, string expectedToken)
    {
        var identity = NetworkOpennessRead.ReadTypedIdentityString(() => value, "Name");

        Assert.False(identity.IsUsable);
        Assert.Equal(expectedToken, identity.SnapshotToken);
    }

    [Theory]
    [InlineData(-1, false, "negative")]
    [InlineData(0, true, "value:0")]
    [InlineData(7, true, "value:7")]
    public void IdentityInt_RejectsNegativeNumbers(int value, bool expectedUsable, string expectedToken)
    {
        var identity = NetworkOpennessRead.ReadTypedIdentityInt(() => value, "Number");

        Assert.Equal(expectedUsable, identity.IsUsable);
        Assert.Equal(expectedToken, identity.SnapshotToken);
        if (expectedUsable) Assert.Equal(value, identity.Value);
    }

    [Fact]
    public void OptionalValues_PreserveFalseSignedNumbersAndEnumFormatting()
    {
        var messages = new List<string>();

        Assert.False(NetworkOpennessRead.ReadOptionalBool(() => false, "validity", messages));
        Assert.Equal(-1, NetworkOpennessRead.ReadOptionalInt(() => -1, "offset", messages));
        Assert.Equal(0, NetworkOpennessRead.ReadOptionalNonNegativeInt(() => 0, "length", messages));
        Assert.Equal("Read, Write", NetworkOpennessRead.ReadOptionalEnumName(
            () => Access.Read | Access.Write, "access", messages));
        Assert.True(NetworkOpennessRead.TryReadEnumName(() => (Access)8, out var numeric, out var error));
        Assert.Equal("8", numeric);
        Assert.Null(error);
        Assert.Empty(messages);
    }

    [Fact]
    public void NonNegativeInt_ReportsANegativeValueWithoutChangingSignedReads()
    {
        var messages = new List<string>();

        Assert.Null(NetworkOpennessRead.ReadOptionalNonNegativeInt(() => -1, "length", messages));

        Assert.Equal(new[] { "Could not read length: the reported value was negative." }, messages);
    }

    [Fact]
    public void ExactStringAttributes_NeverCoerceValuesToStrings()
    {
        var messages = new List<string>();
        var invalid = new AttributeObject(() => new ThrowsOnToString());

        Assert.Null(NetworkOpennessRead.ReadExactStringAttribute(invalid, "Name", "name", messages));
        Assert.Single(messages);
        Assert.Contains("unsupported CLR type", messages[0], StringComparison.Ordinal);
        Assert.False(NetworkOpennessRead.TryReadStringAttribute(invalid, "Name", out var value, out var error));
        Assert.Null(value);
        Assert.NotNull(error);
        var identity = NetworkOpennessRead.ReadExactStringIdentityAttribute(invalid, "Name", "Name");
        Assert.False(identity.IsUsable);
        Assert.Equal("wrongType", identity.SnapshotToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Device_A")]
    public void ExactStringAttributes_PreserveNullAndRawText(string? expected)
    {
        var messages = new List<string>();
        var source = new AttributeObject(() => expected);

        Assert.True(NetworkOpennessRead.TryReadStringAttribute(source, "Name", out var value, out var error));
        Assert.Equal(expected, value);
        Assert.Null(error);
        Assert.Equal(expected, NetworkOpennessRead.ReadExactStringAttribute(source, "Name", "name", messages));
        Assert.Empty(messages);
    }

    [Fact]
    public void TryReads_ReportRecoverableErrorsWithoutInventingValues()
    {
        var failure = new EngineeringNotSupportedException("unavailable");

        Assert.False(NetworkOpennessRead.TryReadStringAttribute(
            new AttributeObject(() => throw failure), "Name", out var text, out var textError));
        Assert.Null(text);
        Assert.Equal("unavailable", textError);
        Assert.False(NetworkOpennessRead.TryReadEnumName<Access>(() => throw failure, out var enumName, out var enumError));
        Assert.Null(enumName);
        Assert.Equal("unavailable", enumError);
        Assert.Null(NetworkOpennessRead.ReadOptionalString(() => throw failure, "name", null));
    }

    [Fact]
    public void CombineDiagnostics_DropsWhitespaceAndOrdinalDuplicatesInFirstSeenOrder()
    {
        Assert.Equal(new[] { "first", "second", "First" }, NetworkOpennessRead.CombineDiagnostics(
            new[] { "first", "", " ", "second" }, "second", "First", "\t"));
    }

    private static object? ReadFailure(string kind, Exception failure, List<string> messages)
        => kind switch
        {
            "string" => NetworkOpennessRead.ReadOptionalString(() => throw failure, "field", messages),
            "bool" => NetworkOpennessRead.ReadOptionalBool(() => throw failure, "field", messages),
            "int" => NetworkOpennessRead.ReadOptionalInt(() => throw failure, "field", messages),
            "nonNegativeInt" => NetworkOpennessRead.ReadOptionalNonNegativeInt(() => throw failure, "field", messages),
            "enum" => NetworkOpennessRead.ReadOptionalEnumName<Access>(() => throw failure, "field", messages),
            "attribute" => NetworkOpennessRead.ReadExactStringAttribute(new AttributeObject(() => throw failure), "Name", "field", messages),
            "identityString" => NetworkOpennessRead.ReadTypedIdentityString(() => throw failure, "Name"),
            "identityInt" => NetworkOpennessRead.ReadTypedIdentityInt(() => throw failure, "Number"),
            "identityAttribute" => NetworkOpennessRead.ReadExactStringIdentityAttribute(new AttributeObject(() => throw failure), "Name", "Name"),
            "tryAttribute" => NetworkOpennessRead.TryReadStringAttribute(new AttributeObject(() => throw failure), "Name", out _, out _),
            "tryEnum" => NetworkOpennessRead.TryReadEnumName<Access>(() => throw failure, out _, out _),
            _ => throw new ArgumentException(kind),
        };

    private sealed class AttributeObject(Func<object?> read) : IEngineeringObject
    {
        public IEngineeringObject? Parent => null;
        public object GetAttribute(string name) => read()!;
    }

    private sealed class ThrowsOnToString
    {
        public override string ToString() => throw new InvalidOperationException("Must not coerce identity values.");
    }

    [Flags]
    private enum Access { Read = 1, Write = 2 }
}
