using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class ProjectTreeSafetyPayloadContractTests
{
    private static readonly ProjectTreeOwnerScopeInfo Owner = new("Plc", "PLC_1", null, "PLC_1/Blocks");

    [Theory]
    [InlineData("UserBlockGroup", "UserBlockGroup", "Mixer")]
    [InlineData("UserBlockGroup", "UserBlockGroup", "mixer")]
    [InlineData("UserBlockGroup", "UserBlockGroup", "Other")]
    [InlineData("FB", "FB", "Mixer")]
    [InlineData("FB", "FC", "Mixer")]
    [InlineData("FB", "UserBlockGroup", "Other")]
    public void DecodeCreateBlockGroup_RejectsAmbiguousOccupancyCandidates(string firstKind, string secondKind, string secondName)
    {
        var snapshot = new CreateBlockGroupSafetySnapshotInfo(Owner, Owner.RootBlocksPath, [],
            [new(firstKind, "Mixer", Owner.RootBlocksPath + "/Mixer"),
             new(secondKind, secondName, Owner.RootBlocksPath + "/" + secondName)]);
        Assert.Throws<JsonException>(() => ProjectTreeSafetyPayloadContract.DecodeCreateBlockGroupAndCanonicalize(Serialize(snapshot)));
    }

    [Fact]
    public void DecodeCreateBlock_RejectsDuplicateGroupOccupancies()
    {
        var group = new ProjectTreeOccupancyInfo("UserBlockGroup", "Mixer", Owner.RootBlocksPath + "/Mixer");
        var snapshot = new CreateBlockSafetySnapshotInfo(Owner, Owner.RootBlocksPath, [], [group, group], null);
        Assert.Throws<JsonException>(() => ProjectTreeSafetyPayloadContract.DecodeCreateBlockAndCanonicalize(Serialize(snapshot)));
    }

    [Fact]
    public void CreateSnapshots_AllowBlockAndGroupOccupancyInSeparateNamespaces()
    {
        var path = Owner.RootBlocksPath + "/Mixer";
        ProjectTreeOccupancyInfo[] occupancies = [new("FB", "Mixer", path), new("UserBlockGroup", "Mixer", path)];
        var blockSnapshot = new CreateBlockSafetySnapshotInfo(Owner, Owner.RootBlocksPath, [], occupancies,
            new("Mixer", path, "FB", "xml", "hash", "<Document/>"));
        var groupSnapshot = new CreateBlockGroupSafetySnapshotInfo(Owner, Owner.RootBlocksPath, [], occupancies);
        Assert.NotEmpty(ProjectTreeSafetyPayloadContract.DecodeCreateBlockAndCanonicalize(Serialize(blockSnapshot)));
        Assert.NotEmpty(ProjectTreeSafetyPayloadContract.DecodeCreateBlockGroupAndCanonicalize(Serialize(groupSnapshot)));
    }

    [Theory]
    [InlineData(false, "FB", "Mixer", "before")]
    [InlineData(false, "FB", "Mixer", "after")]
    [InlineData(false, "FC", "Mixer", "before")]
    [InlineData(false, "FB", "mixer", "before")]
    [InlineData(true, "FB", "Mixer", "after")]
    [InlineData(true, "FC", "Mixer", "before")]
    public void DecodeDeleteBlockGroup_RejectsDuplicateDescendantCandidatesRecursively(bool nested, string secondKind, string secondName, string secondContent)
    {
        var groupPath = Owner.RootBlocksPath + "/AreaA";
        var parent = nested ? groupPath + "/Child" : groupPath;
        ProjectTreeGroupDescendantInfo[] children =
        [
            new("FB", "Mixer", parent + "/Mixer", "before-hash", "before", []),
            new(secondKind, secondName, parent + "/" + secondName, secondContent + "-hash", secondContent, [])
        ];
        ProjectTreeGroupDescendantInfo[] descendants = nested
            ? [new("UserBlockGroup", "Child", parent, null, null, children)] : children;
        var snapshot = new DeleteBlockGroupSafetySnapshotInfo(Owner, Owner.RootBlocksPath, groupPath, [], descendants);
        Assert.Throws<JsonException>(() => ProjectTreeSafetyPayloadContract.DecodeDeleteBlockGroupAndCanonicalize(Serialize(snapshot)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecodeDeleteBlockGroup_RejectsDuplicateGroupCandidatesRecursively(bool nested)
    {
        var groupPath = Owner.RootBlocksPath + "/AreaA";
        var parent = nested ? groupPath + "/Nested" : groupPath;
        var duplicate = new ProjectTreeGroupDescendantInfo("UserBlockGroup", "Child", parent + "/Child", null, null, []);
        ProjectTreeGroupDescendantInfo[] descendants = nested
            ? [new("UserBlockGroup", "Nested", parent, null, null, [duplicate, duplicate])]
            : [duplicate, duplicate];
        var snapshot = new DeleteBlockGroupSafetySnapshotInfo(Owner, Owner.RootBlocksPath, groupPath, [], descendants);
        Assert.Throws<JsonException>(() => ProjectTreeSafetyPayloadContract.DecodeDeleteBlockGroupAndCanonicalize(Serialize(snapshot)));
    }

    [Fact]
    public void DecodeDeleteBlockGroup_AllowsBlockGroupNamespaceOverlapAndRepeatedNamesUnderDifferentParents()
    {
        var groupPath = Owner.RootBlocksPath + "/AreaA";
        var path = groupPath + "/Mixer";
        var snapshot = new DeleteBlockGroupSafetySnapshotInfo(Owner, Owner.RootBlocksPath, groupPath, [],
        [
            new("FB", "Mixer", path, "hash", "before", []),
            new("UserBlockGroup", "Mixer", path, null, null,
                [new("FB", "Mixer", path + "/Mixer", "hash", "before", [])])
        ]);
        Assert.NotEmpty(ProjectTreeSafetyPayloadContract.DecodeDeleteBlockGroupAndCanonicalize(Serialize(snapshot)));
    }

    private static string Serialize<T>(T snapshot)
        => JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public void DecodeCreateBlockAndCanonicalize_RetainsSoftwareUnitOwnerAndOccupiedContent()
    {
        const string payload = """
        {
          "owner": {
            "scopeKind": "SoftwareUnit",
            "plcName": "PLC_1",
            "softwareUnitName": "Line1",
            "rootBlocksPath": "PLC_1/Units/Line1/Blocks"
          },
          "parentPath": "PLC_1/Units/Line1/Blocks/Motion",
          "ancestors": [
            { "name": "Motion", "path": "PLC_1/Units/Line1/Blocks/Motion", "kind": "UserBlockGroup" }
          ],
          "occupancies": [
            { "kind": "FB", "name": "Mixer", "path": "PLC_1/Units/Line1/Blocks/Motion/Mixer" }
          ],
          "occupiedBlock": {
            "name": "Mixer",
            "path": "PLC_1/Units/Line1/Blocks/Motion/Mixer",
            "blockKind": "FB",
            "format": "xml",
            "contentSha256": "abc",
            "content": "<Document>v1</Document>"
          }
        }
        """;

        var canonical = ProjectTreeSafetyPayloadContract.DecodeCreateBlockAndCanonicalize(payload);

        Assert.Contains("\"softwareUnitName\":\"Line1\"", canonical, StringComparison.Ordinal);
        Assert.Contains("\"parentPath\":\"PLC_1/Units/Line1/Blocks/Motion\"", canonical, StringComparison.Ordinal);
        using var canonicalDocument = JsonDocument.Parse(canonical);
        Assert.Equal(
            "<Document>v1</Document>",
            canonicalDocument.RootElement.GetProperty("occupiedBlock").GetProperty("content").GetString());
    }

    [Fact]
    public void DecodeCreateBlockGroupAndCanonicalize_RejectsMissingOwner()
    {
        const string payload = """
        {
          "parentPath": "PLC_1/Blocks/Main",
          "ancestors": [],
          "occupancies": []
        }
        """;

        Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeCreateBlockGroupAndCanonicalize(payload));
    }

    [Fact]
    public void DecodeDeleteBlockGroupAndCanonicalize_RejectsBlankDescendantContent()
    {
        const string payload = """
        {
          "owner": {
            "scopeKind": "Plc",
            "plcName": "PLC_1",
            "softwareUnitName": null,
            "rootBlocksPath": "PLC_1/Blocks"
          },
          "parentPath": "PLC_1/Blocks/Main",
          "groupPath": "PLC_1/Blocks/Main/AreaA",
          "ancestors": [
            { "name": "Main", "path": "PLC_1/Blocks/Main", "kind": "UserBlockGroup" }
          ],
          "descendants": [
            {
              "kind": "FB",
              "name": "Mixer",
              "path": "PLC_1/Blocks/Main/AreaA/Mixer",
              "contentSha256": "abc",
              "content": "",
              "children": []
            }
          ]
        }
        """;

        var exception = Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeDeleteBlockGroupAndCanonicalize(payload));
        Assert.Contains("content", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"owner":{"scopeKind":"Nope","plcName":"PLC_1","softwareUnitName":null,"rootBlocksPath":"PLC_1/Blocks"},"parentPath":"PLC_1/Blocks","ancestors":[],"occupancies":[]}""")]
    [InlineData("""{"owner":{"scopeKind":"Plc","plcName":"","softwareUnitName":null,"rootBlocksPath":"PLC_1/Blocks"},"parentPath":"PLC_1/Blocks","ancestors":[],"occupancies":[]}""")]
    [InlineData("""{"owner":{"scopeKind":"Plc","plcName":"PLC_1","softwareUnitName":null,"rootBlocksPath":""},"parentPath":"PLC_1/Blocks","ancestors":[],"occupancies":[]}""")]
    public void DecodeCreateBlockGroupAndCanonicalize_RejectsInvalidRequiredOwnerValues(string payload)
    {
        Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeCreateBlockGroupAndCanonicalize(payload));
    }

    [Fact]
    public void DecodeCreateBlockAndCanonicalize_AllowsGroupOccupancyWithoutOccupiedBlock()
    {
        const string payload = """
        {
          "owner":{"scopeKind":"Plc","plcName":"PLC_1","softwareUnitName":null,"rootBlocksPath":"PLC_1/Blocks"},
          "parentPath":"PLC_1/Blocks/Main",
          "ancestors":[{"name":"Main","path":"PLC_1/Blocks/Main","kind":"UserBlockGroup"}],
          "occupancies":[{"kind":"UserBlockGroup","name":"Mixer","path":"PLC_1/Blocks/Main/Mixer"}],
          "occupiedBlock":null
        }
        """;

        var canonical = ProjectTreeSafetyPayloadContract.DecodeCreateBlockAndCanonicalize(payload);

        Assert.Contains("\"occupiedBlock\":null", canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeCreateBlockAndCanonicalize_RejectsBlockOccupancyWithoutOccupiedBlock()
    {
        const string payload = """
        {
          "owner":{"scopeKind":"Plc","plcName":"PLC_1","softwareUnitName":null,"rootBlocksPath":"PLC_1/Blocks"},
          "parentPath":"PLC_1/Blocks/Main",
          "ancestors":[{"name":"Main","path":"PLC_1/Blocks/Main","kind":"UserBlockGroup"}],
          "occupancies":[{"kind":"FB","name":"Mixer","path":"PLC_1/Blocks/Main/Mixer"}],
          "occupiedBlock":null
        }
        """;

        Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeCreateBlockAndCanonicalize(payload));
    }

    [Fact]
    public void DecodeCreateBlockAndCanonicalize_RejectsOccupiedBlockWhoseNameAndPathDoNotMatchBlockOccupancy()
    {
        const string payload = """
        {
          "owner":{"scopeKind":"Plc","plcName":"PLC_1","softwareUnitName":null,"rootBlocksPath":"PLC_1/Blocks"},
          "parentPath":"PLC_1/Blocks/Main",
          "ancestors":[{"name":"Main","path":"PLC_1/Blocks/Main","kind":"UserBlockGroup"}],
          "occupancies":[{"kind":"FB","name":"Mixer","path":"PLC_1/Blocks/Main/Mixer"}],
          "occupiedBlock":{"name":"Pump","path":"PLC_1/Blocks/Main/Pump","blockKind":"FB","format":"xml","contentSha256":"abc","content":"<Document/>"}
        }
        """;

        var exception = Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeCreateBlockAndCanonicalize(payload));
        Assert.Contains("must correspond to the declared block occupancy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeCreateBlockAndCanonicalize_RejectsMultipleBlockOccupancies()
    {
        const string payload = """
        {
          "owner":{"scopeKind":"Plc","plcName":"PLC_1","softwareUnitName":null,"rootBlocksPath":"PLC_1/Blocks"},
          "parentPath":"PLC_1/Blocks/Main",
          "ancestors":[{"name":"Main","path":"PLC_1/Blocks/Main","kind":"UserBlockGroup"}],
          "occupancies":[
            {"kind":"FB","name":"Mixer","path":"PLC_1/Blocks/Main/Mixer"},
            {"kind":"FC","name":"Pump","path":"PLC_1/Blocks/Main/Pump"}
          ],
          "occupiedBlock":{"name":"Mixer","path":"PLC_1/Blocks/Main/Mixer","blockKind":"FB","format":"xml","contentSha256":"abc","content":"<Document/>"}
        }
        """;

        Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeCreateBlockAndCanonicalize(payload));
    }

    [Fact]
    public void DecodeCreateBlockGroupAndCanonicalize_RejectsIncompleteAncestorChain()
    {
        const string payload = """
        {
          "owner":{"scopeKind":"Plc","plcName":"PLC_1","softwareUnitName":null,"rootBlocksPath":"PLC_1/Blocks"},
          "parentPath":"PLC_1/Blocks/Main/AreaA",
          "ancestors":[{"name":"AreaA","path":"PLC_1/Blocks/Main/AreaA","kind":"UserBlockGroup"}],
          "occupancies":[]
        }
        """;

        Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeCreateBlockGroupAndCanonicalize(payload));
    }

    [Fact]
    public void DecodeCreateBlockGroupAndCanonicalize_RejectsOccupancyBelowAChildGroup()
    {
        const string payload = """
        {
          "owner":{"scopeKind":"Plc","plcName":"PLC_1","softwareUnitName":null,"rootBlocksPath":"PLC_1/Blocks"},
          "parentPath":"PLC_1/Blocks/Main",
          "ancestors":[{"name":"Main","path":"PLC_1/Blocks/Main","kind":"UserBlockGroup"}],
          "occupancies":[{"kind":"FB","name":"Mixer","path":"PLC_1/Blocks/Main/Child/Mixer"}]
        }
        """;

        Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeCreateBlockGroupAndCanonicalize(payload));
    }

    [Fact]
    public void DecodeDeleteBlockGroupAndCanonicalize_RejectsGroupBelowAChildGroup()
    {
        const string payload = """
        {
          "owner":{"scopeKind":"Plc","plcName":"PLC_1","softwareUnitName":null,"rootBlocksPath":"PLC_1/Blocks"},
          "parentPath":"PLC_1/Blocks/Main",
          "groupPath":"PLC_1/Blocks/Main/Child/AreaA",
          "ancestors":[{"name":"Main","path":"PLC_1/Blocks/Main","kind":"UserBlockGroup"}],
          "descendants":[]
        }
        """;

        Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeDeleteBlockGroupAndCanonicalize(payload));
    }

    [Fact]
    public void DecodeDeleteBlockGroupAndCanonicalize_RejectsDescendantBelowAnUnreportedGroup()
    {
        const string payload = """
        {
          "owner":{"scopeKind":"Plc","plcName":"PLC_1","softwareUnitName":null,"rootBlocksPath":"PLC_1/Blocks"},
          "parentPath":"PLC_1/Blocks/Main",
          "groupPath":"PLC_1/Blocks/Main/AreaA",
          "ancestors":[{"name":"Main","path":"PLC_1/Blocks/Main","kind":"UserBlockGroup"}],
          "descendants":[{"kind":"FB","name":"Mixer","path":"PLC_1/Blocks/Main/AreaA/Child/Mixer","contentSha256":"abc","content":"<Document/>","children":[]}]
        }
        """;

        Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeDeleteBlockGroupAndCanonicalize(payload));
    }
}
