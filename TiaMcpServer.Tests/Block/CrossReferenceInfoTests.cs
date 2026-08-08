using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class CrossReferenceInfoTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void EmptyReportSerializesWithEmptyPlcList()
    {
        var report = new CrossReferenceReport();

        var roundTripped = RoundTrip(report);

        Assert.Equal(CrossReferenceFilterNames.ObjectsWithReferences, roundTripped.Filter);
        Assert.NotNull(roundTripped.Plcs);
        Assert.Empty(roundTripped.Plcs);
        Assert.Equal(0, roundTripped.TotalSourceCount);
        Assert.Equal(0, roundTripped.TotalReferenceCount);
        Assert.Equal(0, roundTripped.TotalLocationCount);
    }

    [Fact]
    public void FullReportRoundTripsSourceReferenceAndLocation()
    {
        var report = new CrossReferenceReport
        {
            Filter = CrossReferenceFilterNames.AllObjects,
            TotalSourceCount = 1,
            TotalReferenceCount = 1,
            TotalLocationCount = 1,
            Plcs =
            {
                new PlcCrossReferenceInfo
                {
                    PlcName = "PLC_1",
                    SourceCount = 1,
                    ReferenceCount = 1,
                    LocationCount = 1,
                    Sources =
                    {
                        new CrossReferenceSourceInfo
                        {
                            Name = "MotorStart",
                            TypeName = "FC",
                            Path = "PLC_1/Blocks/MotorStart",
                            Device = "PLC_1",
                            Address = "FC1",
                            References =
                            {
                                new CrossReferenceTargetInfo
                                {
                                    Name = "MotorReady",
                                    TypeName = "Bool",
                                    Path = "PLC_1/TagTables/Default tag table/MotorReady",
                                    Device = "PLC_1",
                                    Address = "%I0.0",
                                    Locations =
                                    {
                                        new CrossReferenceLocationInfo
                                        {
                                            Name = "Network 1",
                                            TypeName = "Network",
                                            Address = "1",
                                            Access = "Read",
                                            ReferenceType = "Uses",
                                            ReferenceLocation = "PLC_1/Blocks/MotorStart",
                                            ReferencedAs = "Tag",
                                            ReferencedAsName = "MotorReady"
                                        }
                                    }
                                }
                            },
                            Children =
                            {
                                new CrossReferenceSourceInfo
                                {
                                    Name = "Network 1",
                                    TypeName = "Network",
                                    Path = "PLC_1/Blocks/MotorStart/Network 1"
                                }
                            }
                        }
                    }
                }
            }
        };

        var roundTripped = RoundTrip(report);
        var plc = Assert.Single(roundTripped.Plcs);
        var source = Assert.Single(plc.Sources);
        var reference = Assert.Single(source.References);
        var location = Assert.Single(reference.Locations);
        var child = Assert.Single(source.Children);

        Assert.Equal(CrossReferenceFilterNames.AllObjects, roundTripped.Filter);
        Assert.Equal("PLC_1", plc.PlcName);
        Assert.Equal("MotorStart", source.Name);
        Assert.Equal("FC", source.TypeName);
        Assert.Equal("PLC_1/Blocks/MotorStart", source.Path);
        Assert.Equal("PLC_1", source.Device);
        Assert.Equal("FC1", source.Address);
        Assert.Equal("MotorReady", reference.Name);
        Assert.Equal("%I0.0", reference.Address);
        Assert.Equal("Read", location.Access);
        Assert.Equal("Uses", location.ReferenceType);
        Assert.Equal("MotorReady", location.ReferencedAsName);
        Assert.Equal("Network 1", child.Name);
        Assert.Equal(1, roundTripped.TotalSourceCount);
        Assert.Equal(1, roundTripped.TotalReferenceCount);
        Assert.Equal(1, roundTripped.TotalLocationCount);
    }

    [Fact]
    public void UnusedObjectReportRoundTripsSourcesWithEmptyReferences()
    {
        var report = new CrossReferenceReport
        {
            Filter = CrossReferenceFilterNames.UnusedObjects,
            TotalSourceCount = 1,
            Plcs =
            {
                new PlcCrossReferenceInfo
                {
                    PlcName = "PLC_1",
                    SourceCount = 1,
                    Sources =
                    {
                        new CrossReferenceSourceInfo
                        {
                            Name = "UnusedBlock",
                            TypeName = "FC",
                            Path = "PLC_1/Blocks/UnusedBlock"
                        }
                    }
                }
            }
        };

        var roundTripped = RoundTrip(report);
        var source = Assert.Single(Assert.Single(roundTripped.Plcs).Sources);

        Assert.Equal(CrossReferenceFilterNames.UnusedObjects, roundTripped.Filter);
        Assert.Empty(source.References);
    }

    [Fact]
    public void PlcMessagesRoundTrip()
    {
        var report = new CrossReferenceReport
        {
            Plcs =
            {
                new PlcCrossReferenceInfo
                {
                    PlcName = "PLC_1",
                    Messages = { "Skipped source 'ProtectedBlock': Access denied." }
                }
            }
        };

        var roundTripped = RoundTrip(report);
        var plc = Assert.Single(roundTripped.Plcs);

        Assert.Equal("Skipped source 'ProtectedBlock': Access denied.", Assert.Single(plc.Messages));
    }

    [Fact]
    public void NullOrEmptyFilterDefaultsToObjectsWithReferences()
    {
        Assert.True(CrossReferenceFilterNames.TryNormalize(null, out var nullFilter, out var nullError));
        Assert.True(CrossReferenceFilterNames.TryNormalize(" ", out var emptyFilter, out var emptyError));

        Assert.Equal(CrossReferenceFilterNames.ObjectsWithReferences, nullFilter);
        Assert.Equal(CrossReferenceFilterNames.ObjectsWithReferences, emptyFilter);
        Assert.Null(nullError);
        Assert.Null(emptyError);
    }

    [Fact]
    public void ValidFilterNamesParseCaseInsensitively()
    {
        Assert.True(CrossReferenceFilterNames.TryNormalize("unusedobjects", out var filter, out var error));

        Assert.Equal(CrossReferenceFilterNames.UnusedObjects, filter);
        Assert.Null(error);
    }

    [Fact]
    public void InvalidFilterReturnsAllowedValues()
    {
        Assert.False(CrossReferenceFilterNames.TryNormalize("BlocksOnly", out var filter, out var error));

        Assert.Equal(string.Empty, filter);
        Assert.NotNull(error);
        Assert.Contains("BlocksOnly", error);
        Assert.Contains(CrossReferenceFilterNames.AllObjects, error);
        Assert.Contains(CrossReferenceFilterNames.ObjectsWithReferences, error);
        Assert.Contains(CrossReferenceFilterNames.ObjectsWithoutReferences, error);
        Assert.Contains(CrossReferenceFilterNames.UnusedObjects, error);
    }

    private static CrossReferenceReport RoundTrip(CrossReferenceReport report)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        return JsonSerializer.Deserialize<CrossReferenceReport>(json, JsonOptions)!;
    }
}
