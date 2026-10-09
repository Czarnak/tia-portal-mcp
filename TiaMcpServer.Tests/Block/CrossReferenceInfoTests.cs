using System.Text.Json;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class CrossReferenceInfoTests
{
    private static readonly JsonSerializerOptions JsonOptions = WorkerJson.PayloadOptionsFor(typeof(CrossReferenceReport));

    [Fact]
    public void EmptyReportSerializesWithEmptySourcesAndDefaultFilter()
    {
        var roundTripped = RoundTrip(new CrossReferenceReport());

        Assert.Equal(CrossReferenceFilterNames.ObjectsWithReferences, roundTripped.Filter);
        Assert.Empty(roundTripped.Sources);
        Assert.Empty(roundTripped.Messages);
        Assert.Empty(roundTripped.Target.Path);
        Assert.Null(roundTripped.Target.Member);
        Assert.Equal(0, roundTripped.TotalSourceCount);
        Assert.Equal(0, roundTripped.OmittedSourceCount);
    }

    [Fact]
    public void ReportWritesExplicitNulls()
    {
        var json = JsonSerializer.Serialize(new CrossReferenceReport
        {
            Sources =
            {
                new CrossReferenceSourceInfo
                {
                    References = { new CrossReferenceTargetInfo { Locations = { new CrossReferenceLocationInfo() } } }
                }
            }
        }, JsonOptions);

        Assert.Contains("\"member\":null", json);
        Assert.Contains("\"referencedAs\":null", json);
        Assert.Contains("\"omittedSourceCount\":0", json);
    }

    [Fact]
    public void FullReportRoundTripsTargetSourceReferenceAndLocation()
    {
        var report = new CrossReferenceReport
        {
            Target = new CrossReferenceSelectorInfo
            {
                Path =
                {
                    new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.Device, Name = "Station_1" },
                    new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.PlcSoftware, Name = "PLC_1" },
                    new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.TagTableFolder, Name = "PLC tags" },
                    new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.TagTable, Name = "Default tag table" }
                },
                Member = new CrossReferenceMemberSelectorInfo { Kind = CrossReferenceMemberKinds.Tag, Name = "MotorReady" }
            },
            Filter = CrossReferenceFilterNames.AllObjects,
            IsComplete = false,
            OwnerQueryCount = 3,
            SuccessfulOwnerQueryCount = 2,
            Messages = { "incomplete" },
            TotalSourceCount = 2,
            TotalReferenceCount = 1,
            TotalLocationCount = 1,
            OmittedSourceCount = 4,
            Sources =
            {
                new CrossReferenceSourceInfo
                {
                    Name = "MotorStart", TypeName = "FC", Path = "PLC_1/Blocks/MotorStart", Device = "PLC_1", Address = "FC1",
                    References =
                    {
                        new CrossReferenceTargetInfo
                        {
                            Name = "MotorReady", TypeName = "Bool", Address = "%I0.0",
                            Locations =
                            {
                                new CrossReferenceLocationInfo
                                {
                                    Name = "Network 1", Access = "Read", ReferenceType = "Uses",
                                    ReferencedAs = new CrossReferenceObjectRefInfo { Name = "MotorReady", TypeName = "PlcTag" },
                                    ReferencedAsName = "MotorReady"
                                }
                            }
                        }
                    },
                    Children = { new CrossReferenceSourceInfo { Name = "Network 1" } }
                }
            }
        };

        var roundTripped = RoundTrip(report);
        var source = Assert.Single(roundTripped.Sources);
        var location = Assert.Single(Assert.Single(source.References).Locations);

        Assert.Equal("Default tag table", roundTripped.Target.Path[3].Name);
        Assert.Equal(CrossReferenceMemberKinds.Tag, roundTripped.Target.Member!.Kind);
        Assert.Equal("MotorReady", roundTripped.Target.Member.Name);
        Assert.Equal(CrossReferenceFilterNames.AllObjects, roundTripped.Filter);
        Assert.False(roundTripped.IsComplete);
        Assert.Equal(3, roundTripped.OwnerQueryCount);
        Assert.Equal(2, roundTripped.SuccessfulOwnerQueryCount);
        Assert.Equal("incomplete", Assert.Single(roundTripped.Messages));
        Assert.Equal(4, roundTripped.OmittedSourceCount);
        Assert.Equal("Network 1", Assert.Single(source.Children).Name);
        Assert.Equal("Read", location.Access);
        Assert.Equal("Uses", location.ReferenceType);
        Assert.Equal("PlcTag", location.ReferencedAs!.TypeName);
        Assert.Equal("MotorReady", location.ReferencedAs.Name);
    }

    [Fact]
    public void ClosedNameListsAreDistinctAndContainUnknown()
    {
        Assert.Equal(37, CrossReferenceAccessNames.All.Distinct().Count());
        Assert.Equal(13, CrossReferenceTypeNames.All.Distinct().Count());
        Assert.Contains(CrossReferenceAccessNames.Unknown, CrossReferenceAccessNames.All);
        Assert.Contains(CrossReferenceTypeNames.Unknown, CrossReferenceTypeNames.All);
        Assert.Equal(new[] { "Tag", "SystemConstant", "UserConstant" }, CrossReferenceMemberKinds.All);
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
        foreach (var allowed in CrossReferenceFilterNames.Allowed) Assert.Contains(allowed, error);
    }

    private static CrossReferenceReport RoundTrip(CrossReferenceReport report)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        return JsonSerializer.Deserialize<CrossReferenceReport>(json, JsonOptions)!;
    }
}
