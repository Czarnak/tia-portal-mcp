using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests;

public sealed class NetworkAttributeResultBuilderTests
{
    [Fact]
    public void Build_WithoutRequestedNames_UsesOrdinalNameOrderAndSortedSupportedTypes()
    {
        var attributes = NetworkAttributeResultBuilder.Build(
            new[]
            {
                Modeled("zeta", () => 1, supportedTypes: new[] { "System.String", "System.Int32" }),
                Modeled("Alpha", () => 2, supportedTypes: new[] { "System.Boolean" }),
            },
            new[]
            {
                Dynamic("zeta", () => 1, supportedTypes: new[] { "System.Int32", "System.Decimal" }),
            });

        Assert.Collection(
            attributes,
            alpha => Assert.Equal("Alpha", alpha.Name),
            zeta =>
            {
                Assert.Equal("zeta", zeta.Name);
                Assert.Equal("modeledAndDynamic", zeta.Source);
                Assert.Equal(new[] { "System.Decimal", "System.Int32", "System.String" }, zeta.SupportedTypes);
            });
    }

    [Fact]
    public void Build_WithRequestedNames_EmitsExactlyOneEntryPerNameInRequestOrder()
    {
        var attributes = NetworkAttributeResultBuilder.Build(
            new[] { Modeled("known", () => 1) },
            Array.Empty<NetworkAttributeObservation>(),
            new[] { "missing", "known" });

        Assert.Collection(
            attributes,
            missing =>
            {
                Assert.Equal("missing", missing.Name);
                Assert.Null(missing.Source);
                Assert.Equal("unknown", missing.Access);
                Assert.Equal("unknownAttribute", missing.Availability);
                Assert.Null(missing.Value);
                Assert.NotNull(missing.Diagnostic);
                Assert.Equal("unknown_attribute", missing.Diagnostic!.Category);
                Assert.Contains("missing", missing.Diagnostic.Message, StringComparison.Ordinal);
            },
            known => Assert.Equal("known", known.Name));
    }

    [Fact]
    public void Build_ModeledAndDynamicValuesDisagree_RetainsModeledValueAndAddsDiagnostic()
    {
        var attribute = Assert.Single(NetworkAttributeResultBuilder.Build(
            new[] { Modeled("name", () => "modeled") },
            new[] { Dynamic("name", () => "dynamic") }));

        Assert.Equal("modeledAndDynamic", attribute.Source);
        Assert.Equal("modeled", attribute.Value!.Value);
        Assert.NotNull(attribute.Diagnostic);
        Assert.Equal("source_disagreement", attribute.Diagnostic!.Category);
    }

    [Theory]
    [InlineData(false, false, "none")]
    [InlineData(true, false, "readOnly")]
    [InlineData(false, true, "writeOnly")]
    [InlineData(true, true, "readWrite")]
    public void Build_AccessMetadata_MapsEveryKnownAccessMode(bool canRead, bool canWrite, string expectedAccess)
    {
        var attribute = Assert.Single(NetworkAttributeResultBuilder.Build(
            new[] { Modeled("name", () => null, canRead, canWrite) },
            Array.Empty<NetworkAttributeObservation>()));

        Assert.Equal(expectedAccess, attribute.Access);
    }

    [Fact]
    public void Build_ModeledAndDynamicAccess_CombineReadAndWriteCapabilities()
    {
        var attribute = Assert.Single(NetworkAttributeResultBuilder.Build(
            new[] { Modeled("name", () => null, canRead: true, canWrite: false) },
            new[] { Dynamic("name", () => null, canRead: false, canWrite: true) }));

        Assert.Equal("readWrite", attribute.Access);
    }

    [Theory]
    [InlineData("notApplicable")]
    [InlineData("unsupported")]
    [InlineData("unreadable")]
    public void Build_NonReadableObservation_PreservesDeclaredAvailability(string availability)
    {
        var attribute = Assert.Single(NetworkAttributeResultBuilder.Build(
            new[] { new NetworkAttributeObservation { Name = "name", Availability = availability } },
            Array.Empty<NetworkAttributeObservation>()));

        Assert.Equal(availability, attribute.Availability);
        Assert.Null(attribute.Value);
    }

    [Fact]
    public void Build_ThrowingReader_ReportsFailureAndContinuesWithFollowingAttributes()
    {
        var attributes = NetworkAttributeResultBuilder.Build(
            new[]
            {
                Modeled("first", () => throw new InvalidOperationException("broken")),
                Modeled("second", () => 7),
            },
            Array.Empty<NetworkAttributeObservation>());

        Assert.Collection(
            attributes,
            first =>
            {
                Assert.Equal("readFailed", first.Availability);
                Assert.Equal("read_error", first.Diagnostic!.Category);
            },
            second =>
            {
                Assert.Equal("available", second.Availability);
                Assert.Equal(7L, second.Value!.Value);
            });
    }

    [Fact]
    public void Build_UnrepresentableValue_ReportsClrTypeWithoutCallingToString()
    {
        var attribute = Assert.Single(NetworkAttributeResultBuilder.Build(
            new[] { Modeled("name", () => ulong.MaxValue) },
            Array.Empty<NetworkAttributeObservation>()));

        Assert.Equal("unrepresentable", attribute.Availability);
        Assert.Null(attribute.Value);
        Assert.Equal(typeof(ulong).FullName, attribute.Diagnostic!.ClrTypeName);
    }

    private static NetworkAttributeObservation Modeled(
        string name,
        Func<object?> readValue,
        bool? canRead = true,
        bool? canWrite = false,
        IReadOnlyList<string>? supportedTypes = null)
        => new()
        {
            Name = name,
            ReadValue = readValue,
            CanRead = canRead,
            CanWrite = canWrite,
            SupportedTypes = supportedTypes ?? Array.Empty<string>(),
        };

    private static NetworkAttributeObservation Dynamic(
        string name,
        Func<object?> readValue,
        bool? canRead = true,
        bool? canWrite = false,
        IReadOnlyList<string>? supportedTypes = null)
        => new()
        {
            Name = name,
            ReadValue = readValue,
            CanRead = canRead,
            CanWrite = canWrite,
            SupportedTypes = supportedTypes ?? Array.Empty<string>(),
        };
}
