using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OpennessWorker.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public sealed class VciContractDecodeTests
{
    public static TheoryData<string, string> Results => new()
    {
        { "VciWorkspaceListInfo", """{"workspaces":[{"name":"W","rootPath":null,"mappingCount":0}]}""" },
        { "VciMappingListInfo", """{"total":1,"offset":0,"limit":100,"mappings":[{"fileKey":{"directory":"\\dir","fileName":"Leaf"},"format":"SimaticML","path":null,"status":{"compareState":"Equal","details":null}}]}""" },
        { "VciSupportedFormatsInfo", """{"formats":["SimaticML"]}""" },
        { "VciPlanInfo", """{"leaves":1,"mappedLeaves":0,"formatFallbacks":0,"workspaceRootPath":null,"workspaceConflict":null,"keyCollisions":[],"unrepresentable":[],"unverifiable":null}""" },
        { "VciWriteOutcomeInfo", """{"leaves":1,"succeeded":0,"failed":1,"skipped":{"equal":0,"alreadyMapped":0,"conflict":0,"targetChanged":0},"formatFallbacks":0,"lastAttempted":{"path":[{"nodeType":"Device","name":"Fixture"}],"operation":"export","format":null},"projectModified":false,"notes":[]}""" },
    };

    [Theory]
    [MemberData(nameof(Results))]
    public void Results_DecodeAndWriteEveryMember(string typeName, string json)
    {
        var type = ResultType(typeName);
        var normalized = Normalize(type, json);
        var decoded = JsonSerializer.Deserialize(normalized, type, WorkerJson.ExplicitNullPayload)!;
        Assert.False(WorkerJson.OmitsNullMembers(type));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(WorkerJson.SerializePayload(decoded))));
    }

    [Theory]
    [MemberData(nameof(Results))]
    public void Results_RejectEveryMissingAndUnmappedMemberIncludingNestedObjects(string typeName, string json)
    {
        var type = ResultType(typeName);
        var root = JsonNode.Parse(json)!;
        foreach (var node in Objects(root))
        {
            foreach (var property in node.ToList())
            {
                node.Remove(property.Key);
                AssertRejected(type, root.ToJsonString());
                node.Add(property.Key, property.Value);
            }
            node.Add("unmapped", true);
            AssertRejected(type, root.ToJsonString());
            node.Remove("unmapped");
        }
    }

    [Fact]
    public void PartialOutcome_SurvivesExceptionAndWorkerEnvelope()
    {
        var outcome = Activator.CreateInstance(ResultType("VciWriteOutcomeInfo"))!;
        var exception = (WorkerOperationException)Activator.CreateInstance(typeof(WorkerOperationException),
            WorkerFailureCategories.WorkerOperationFailed, "leaf failed", null, null, null, outcome, null)!;
        var property = typeof(WorkerOperationException).GetProperty("VciWriteOutcome");
        Assert.NotNull(property);
        Assert.Same(outcome, property.GetValue(exception));
        var response = new WorkerResponse { Success = false, Error = exception.Message, FailureCategory = exception.FailureCategory };
        var wireProperty = typeof(WorkerResponse).GetProperty("VciWriteOutcome");
        Assert.NotNull(wireProperty);
        wireProperty.SetValue(response, outcome);
        var decoded = JsonSerializer.Deserialize<WorkerResponse>(WorkerJson.SerializePayload(response), WorkerJson.Envelope)!;
        Assert.NotNull(wireProperty.GetValue(decoded));
        Assert.DoesNotContain("vciWriteOutcome", WorkerJson.SerializePayload(new WorkerResponse()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("configured_unverified")]
    [InlineData("binding_changed")]
    [InlineData("identity_changed")]
    [InlineData("invalidated")]
    public void PartialOutcome_SurvivesBindingRewraps(string scenario)
    {
        var binding = new ProjectSessionBinding(scenario == "configured_unverified" ? @"C:\fixtures\A" : null);
        var identity = new WorkerSessionIdentity { ProjectPath = @"C:\fixtures\A", WorkerSessionId = "worker", SessionGeneration = 1, PortalProcessId = 123 };
        if (scenario != "configured_unverified") Assert.True(binding.BindVerified(identity, false, out _));
        if (scenario == "invalidated") binding.Invalidate("fixture");
        var before = binding.CaptureSnapshot();
        if (scenario == "binding_changed") binding.Invalidate("changed during call");
        using var client = new OpennessWorkerClient(binding);
        var property = typeof(WorkerCallResult).GetProperty("VciWriteOutcome");
        Assert.NotNull(property);
        var result = WorkerCallResult.Ok("{}");
        var outcome = Activator.CreateInstance(ResultType("VciWriteOutcomeInfo"));
        property.SetValue(result, outcome);
        var validate = typeof(OpennessWorkerClient).GetMethod("ValidateOrPromoteSessionIdentity", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var rejected = (WorkerCallResult)validate.Invoke(client, new object[] { result, before })!;
        Assert.False(rejected.Success);
        Assert.Same(outcome, property.GetValue(rejected));
        Assert.Same(outcome, property.GetValue(rejected with { Error = "copy" }));
    }

    [Fact]
    public void Request_ContainsOnlyDomainLocalBreadcrumbFields()
    {
        var type = ResultType("VciWriteRequestInfo");
        var write = Activator.CreateInstance(type)!;
        type.GetProperty("RequestId")!.SetValue(write, "request");
        type.GetProperty("BreadcrumbPath")!.SetValue(write, @"C:\fixtures\breadcrumb.json");
        var request = new WorkerRequest();
        var property = typeof(WorkerRequest).GetProperty("VciWrite");
        Assert.NotNull(property);
        property.SetValue(request, write);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, WorkerJson.Request));
        Assert.Equal("request", json.RootElement.GetProperty("vciWrite").GetProperty("requestId").GetString());
        Assert.False(json.RootElement.TryGetProperty("requestId", out _));
        Assert.False(json.RootElement.TryGetProperty("breadcrumbPath", out _));
        Assert.NotNull(typeof(WorkerRequest).GetProperty("VciQuery"));
    }

    private static Type ResultType(string name)
    {
        var type = typeof(VciScope).Assembly.GetType("TiaMcpServer.Contracts.Vci." + name);
        Assert.NotNull(type);
        return type;
    }

    private static string Normalize(Type type, string json)
    {
        var result = typeof(CanonicalJson).GetMethod(nameof(CanonicalJson.NormalizeWorkerPayload))!
            .MakeGenericMethod(type).Invoke(null, new object?[] { json, null })!;
        return (string)result.GetType().GetField("Item2")!.GetValue(result)!;
    }

    private static void AssertRejected(Type type, string json)
        => Assert.IsType<JsonException>(Assert.Throws<TargetInvocationException>(() => Normalize(type, json)).InnerException);

    private static IEnumerable<JsonObject> Objects(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (var child in obj.ToList().Where(property => property.Value is not null))
                foreach (var descendant in Objects(child.Value!)) yield return descendant;
        }
        else if (node is JsonArray array)
            foreach (var child in array.Where(child => child is not null))
                foreach (var descendant in Objects(child!)) yield return descendant;
    }
}
