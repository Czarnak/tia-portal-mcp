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
          "ancestors": [],
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

        Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeDeleteBlockGroupAndCanonicalize(payload));
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
}
