using System.Text.Json;
using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class ProjectTreeSafetyPayloadContractTests
{
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
