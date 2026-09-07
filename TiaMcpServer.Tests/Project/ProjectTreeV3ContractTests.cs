using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectTreeV3ContractTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt = CreatedAt.AddMinutes(10);

    [Fact]
    public void SuccessEnvelope_UsesTheExactV3Shape()
    {
        var response = new BrowseProjectTreeResponse(
            ProjectTreeContract.Version,
            ProjectTreeStatuses.Succeeded,
            new BrowseProjectTreeResult(
                new ProjectTreeSnapshotMetadata("snapshot-1", CreatedAt, ExpiresAt, 1),
                new ProjectTreeQuery(@"C:\Projects\Plant.ap21", null, null),
                new ProjectTreePagination(0, 100, 1, null),
                new[]
                {
                    new ProjectTreeFlatNode(
                        "n0",
                        null,
                        0,
                        "PLC_1",
                        ProjectTreeNodeTypes.Device,
                        new Dictionary<string, string>())
                }),
            Failure: null,
            Warnings: Array.Empty<string>());

        using var document = JsonDocument.Parse(CanonicalJson.Serialize(response));
        var root = document.RootElement;

        Assert.Equal("3.0", root.GetProperty("contractVersion").GetString());
        Assert.Equal("succeeded", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("failure").ValueKind);
        AssertExactPropertyNames(root, "contractVersion", "status", "result", "failure", "warnings");
        Assert.Empty(root.GetProperty("warnings").EnumerateArray());

        var result = root.GetProperty("result");
        AssertExactPropertyNames(result, "snapshot", "query", "pagination", "nodes");
        var snapshot = result.GetProperty("snapshot");
        AssertExactPropertyNames(snapshot, "snapshotId", "createdAt", "idleExpiresAt", "totalNodes");
        var query = result.GetProperty("query");
        AssertExactPropertyNames(query, "projectPath", "startSelector", "depth");
        Assert.Equal(JsonValueKind.Null, query.GetProperty("startSelector").ValueKind);
        Assert.Equal(JsonValueKind.Null, query.GetProperty("depth").ValueKind);
        var pagination = result.GetProperty("pagination");
        AssertExactPropertyNames(pagination, "offset", "requestedPageSize", "returnedCount", "nextCursor");
        Assert.Equal(JsonValueKind.Null, pagination.GetProperty("nextCursor").ValueKind);

        var node = Assert.Single(result.GetProperty("nodes").EnumerateArray());
        AssertExactPropertyNames(node, "nodeId", "parentNodeId", "sequence", "name", "nodeType", "details");
        Assert.Equal(JsonValueKind.Null, node.GetProperty("parentNodeId").ValueKind);
        Assert.Empty(node.GetProperty("details").EnumerateObject());
    }

    [Fact]
    public void FailureEnvelope_UsesExplicitNullResult()
    {
        var response = new BrowseProjectTreeResponse(
            ProjectTreeContract.Version,
            ProjectTreeStatuses.Failed,
            Result: null,
            new BrowseProjectTreeFailure(WorkerFailureCategories.InvalidSelector, "The selector is invalid."),
            Warnings: Array.Empty<string>());

        using var document = JsonDocument.Parse(CanonicalJson.Serialize(response));

        AssertExactPropertyNames(document.RootElement, "contractVersion", "status", "result", "failure", "warnings");
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("result").ValueKind);
        var failure = document.RootElement.GetProperty("failure");
        AssertExactPropertyNames(failure, "category", "message");
        Assert.Equal(WorkerFailureCategories.InvalidSelector, failure.GetProperty("category").GetString());
    }

    [Theory]
    [InlineData("startPath")]
    [InlineData("deviceName")]
    [InlineData("plcName")]
    public void SelectorSegment_RejectsUnknownMembers(string unknownProperty)
    {
        var json = $"{{\"nodeType\":\"Device\",\"name\":\"PLC_1\",\"{unknownProperty}\":\"legacy\"}}";

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProjectTreeSelectorSegment>(json, options));
    }

    [Fact]
    public void SelectorSegment_SerializesWithExactlyNodeTypeAndName()
    {
        var segment = new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.Device, Name = "PLC_1" };

        using var document = JsonDocument.Parse(CanonicalJson.Serialize(segment));

        AssertExactPropertyNames(document.RootElement, "nodeType", "name");
    }

    [Fact]
    public void WorkerRequest_UsesOnlyTheTypedStartSelector()
    {
        var request = new WorkerRequest
        {
            StartSelector = new List<ProjectTreeSelectorSegment>
            {
                new() { NodeType = ProjectTreeNodeTypes.Device, Name = "PLC_1" }
            }
        };

        using var document = JsonDocument.Parse(CanonicalJson.Serialize(request));

        Assert.Null(typeof(WorkerRequest).GetProperty("StartPath"));
        Assert.False(document.RootElement.TryGetProperty("startPath", out _));
        var selector = Assert.Single(document.RootElement.GetProperty("startSelector").EnumerateArray());
        AssertExactPropertyNames(selector, "nodeType", "name");
        Assert.Equal(ProjectTreeNodeTypes.Device, selector.GetProperty("nodeType").GetString());
        Assert.Equal("PLC_1", selector.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(WorkerFailureCategories.InvalidSelector)]
    [InlineData(WorkerFailureCategories.SnapshotTooLarge)]
    [InlineData(WorkerFailureCategories.SnapshotUnavailable)]
    [InlineData(WorkerFailureCategories.ResultItemTooLarge)]
    [InlineData(WorkerFailureCategories.ResultMetadataTooLarge)]
    public void ProjectTreeFailureCategory_IsKnownAndAcceptedByWorkerCallResult(string category)
    {
        Assert.True(WorkerFailureCategories.IsKnown(category));

        var result = WorkerCallResult.Fail(category, "expected test failure");

        Assert.Equal(category, result.FailureCategory);
    }

    [Fact]
    public void NodeTypeVocabulary_IsTheExactClosedSet()
    {
        var expected = new[]
        {
            "Device",
            "PlcSoftware",
            "SoftwareUnit",
            "BlockFolder",
            "SystemBlockFolder",
            "OB",
            "FB",
            "FC",
            "GlobalDB",
            "InstanceDB",
            "ArrayDB",
            "Block",
            "TagTableFolder",
            "TagTable",
            "TypeFolder",
            "Type"
        };

        Assert.Equal(
            expected.OrderBy(value => value, StringComparer.Ordinal),
            ProjectTreeNodeTypes.All.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static void AssertExactPropertyNames(JsonElement element, params string[] expected)
    {
        var actual = element.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
    }
}
