using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationSafetySnapshotContractTests
{
    [Fact]
    public void UpdateTagSnapshot_PreservesFalseFlagsAndEffectiveRename()
    {
        var payload = """
        {
          "targetTable":{"plcName":"PLC_1","folderPath":"","tableName":"Inputs","canonicalPath":"PLC_1/Tag tables/Inputs"},
          "targetTag":{"plcName":"PLC_1","folderPath":"","tableName":"Inputs","tagName":"Start","canonicalPath":"PLC_1/Tag tables/Inputs/Start","dataType":"Bool","logicalAddress":"%I0.0","externalAccessible":false,"externalVisible":false,"externalWritable":false},
          "effectiveName":"Start_1",
          "effectiveLogicalAddress":"%I0.1",
          "nameCollisions":[{"kind":"tag-name","candidateName":"Start_1","canonicalPath":"PLC_1/Tag tables/Inputs/Start_1","logicalAddress":"%I0.1","isTarget":false}],
          "addressCollisions":[{"kind":"logical-address","candidateName":"Other","canonicalPath":"PLC_1/Tag tables/Inputs/Other","logicalAddress":"%I0.1","isTarget":false}]
        }
        """;

        var result = TagOperationSafetySnapshotContract.Decode("update_tag", payload);

        Assert.True(result.Success, result.Error);
        Assert.Contains("\"externalAccessible\":false", result.CanonicalState, StringComparison.Ordinal);
        Assert.Contains("\"externalVisible\":false", result.CanonicalState, StringComparison.Ordinal);
        Assert.Contains("\"externalWritable\":false", result.CanonicalState, StringComparison.Ordinal);
        Assert.Contains("\"effectiveName\":\"Start_1\"", result.CanonicalState, StringComparison.Ordinal);
        Assert.Contains("\"effectiveLogicalAddress\":\"%I0.1\"", result.CanonicalState, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteTagTableSnapshot_RequiresFullExport()
    {
        var payload = """
        {
          "targetTable":{"plcName":"PLC_1","folderPath":"","tableName":"Inputs","canonicalPath":"PLC_1/Tag tables/Inputs"},
          "exportedSimaticMl":"<Document />",
          "exportSha256":"abc123",
          "characterCount":12
        }
        """;

        var result = TagOperationSafetySnapshotContract.Decode("delete_tag_table", payload);

        Assert.True(result.Success, result.Error);
        using var canonical = JsonDocument.Parse(result.CanonicalState);
        Assert.Equal("<Document />", canonical.RootElement.GetProperty("exportedSimaticMl").GetString());
    }

    [Fact]
    public void MalformedPayload_FailsClosedAsProtocolError()
    {
        var result = TagOperationSafetySnapshotContract.Decode("create_tag", "{\"targetTable\":42}");

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.ProtocolError, result.FailureCategory);
    }

    [Fact]
    public void OmittedRequiredSnapshotMembers_FailClosedAsProtocolError()
    {
        var result = TagOperationSafetySnapshotContract.Decode("create_tag", "{}");

        Assert.False(result.Success);
        Assert.Empty(result.CanonicalState);
        Assert.Equal(WorkerFailureCategories.ProtocolError, result.FailureCategory);
    }

    [Fact]
    public void ExplicitNullRequiredSnapshotMember_FailsClosedAsProtocolError()
    {
        var result = TagOperationSafetySnapshotContract.Decode("delete_tag_table", """
        {
          "targetTable":null,
          "exportedSimaticMl":"<Document />",
          "exportSha256":"abc123",
          "characterCount":12
        }
        """);

        Assert.False(result.Success);
        Assert.Empty(result.CanonicalState);
        Assert.Equal(WorkerFailureCategories.ProtocolError, result.FailureCategory);
    }

    [Fact]
    public void UnsupportedCollisionKind_FailsClosedAsProtocolError()
    {
        var result = TagOperationSafetySnapshotContract.Decode("create_tag", """
        {
          "targetTable":{"plcName":"PLC_1","folderPath":"","tableName":"Inputs","canonicalPath":"PLC_1/Tag tables/Inputs"},
          "effectiveName":"Start",
          "effectiveLogicalAddress":null,
          "nameCollisions":[{"kind":"unexpected","candidateName":"Start","canonicalPath":"PLC_1/Tag tables/Inputs/Start","logicalAddress":null,"isTarget":false}],
          "addressCollisions":[]
        }
        """);

        Assert.False(result.Success);
        Assert.Empty(result.CanonicalState);
        Assert.Equal(WorkerFailureCategories.ProtocolError, result.FailureCategory);
    }

    [Theory]
    [InlineData("create_user_constant")]
    [InlineData("update_user_constant")]
    [InlineData("create_tag_table")]
    public void UnknownCollisionKind_IsRejectedForConstantsAndTables(string operation)
    {
        var table = new TagTableSafetyIdentityInfo("PLC_1", "/", "Inputs", "PLC_1/Tag tables/Inputs");
        var collisions = new[] { new TagCollisionProbeInfo("unexpected", "Start", "opaque", null, false) };
        object snapshot = operation switch
        {
            "create_tag_table" => new CreateTagTableSafetySnapshotInfo("PLC_1", "/", "Inputs", collisions),
            "create_user_constant" => new CreateUserConstantSafetySnapshotInfo(table, "Start", collisions),
            _ => new UpdateUserConstantSafetySnapshotInfo(table,
                new UserConstantSafetyIdentityInfo("PLC_1", "/", "Inputs", "Start", "PLC_1/Tag tables/Inputs/Start", "Int", "25"),
                "Start", collisions)
        };
        var result = TagOperationSafetySnapshotContract.Decode(operation,
            TiaMcpServer.Json.CanonicalJson.Serialize(snapshot));
        Assert.False(result.Success);
        Assert.Empty(result.CanonicalState);
        Assert.Equal(WorkerFailureCategories.ProtocolError, result.FailureCategory);
    }
}
