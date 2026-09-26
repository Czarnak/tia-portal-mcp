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
            IsComplete = true,
            TotalSourceCount = 2,
            TotalReferenceCount = 1,
            TotalLocationCount = 1,
            Plcs =
            {
                new PlcCrossReferenceInfo
                {
                    PlcName = "PLC_1",
                    DeviceName = "Station_1",
                    OwnerQueryCount = 1,
                    SuccessfulOwnerQueryCount = 1,
                    IsComplete = true,
                    SourceCount = 2,
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
        Assert.Equal(2, roundTripped.TotalSourceCount);
        Assert.Equal(2, plc.SourceCount);
        Assert.Equal("Station_1", plc.DeviceName);
        Assert.Equal(1, plc.OwnerQueryCount);
        Assert.Equal(1, plc.SuccessfulOwnerQueryCount);
        Assert.True(plc.IsComplete);
        Assert.True(roundTripped.IsComplete);
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

    [Theory]
    [InlineData(true, 0, 1, 1)]
    [InlineData(false, 1, 3, 1)]
    public void CoverageMetadataRoundTrips(bool complete, int sources, int attempted, int successful)
    {
        var report = RoundTrip(new CrossReferenceReport
        {
            IsComplete = complete,
            TotalSourceCount = sources,
            Plcs =
            {
                new PlcCrossReferenceInfo
                {
                    PlcName = "PLC_DP", DeviceName = "Station_1", IsComplete = complete,
                    SourceCount = sources, OwnerQueryCount = attempted, SuccessfulOwnerQueryCount = successful
                }
            }
        });
        var plc = Assert.Single(report.Plcs);
        Assert.Equal(complete, report.IsComplete);
        Assert.Equal(complete, plc.IsComplete);
        Assert.Equal(attempted, plc.OwnerQueryCount);
        Assert.Equal(successful, plc.SuccessfulOwnerQueryCount);
        Assert.Equal(sources, report.TotalSourceCount);
        Assert.Equal("PLC_DP", plc.PlcName);
        Assert.Equal("Station_1", plc.DeviceName);
    }

    [Fact]
    public void OldJsonDefaultsToUnknownIncompleteCoverage()
    {
        var report = JsonSerializer.Deserialize<CrossReferenceReport>(
            """{"plcs":[{"plcName":"legacy","sources":[]}]}""", JsonOptions)!;
        var plc = Assert.Single(report.Plcs);
        Assert.False(report.IsComplete);
        Assert.False(plc.IsComplete);
        Assert.Null(plc.DeviceName);
        Assert.Equal(0, plc.OwnerQueryCount);
        Assert.Equal(0, plc.SuccessfulOwnerQueryCount);
    }

    private static CrossReferenceReport RoundTrip(CrossReferenceReport report)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        return JsonSerializer.Deserialize<CrossReferenceReport>(json, JsonOptions)!;
    }
}
