using System.Text.Json.Nodes;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

public sealed class ProjectBindingInspectionTests
{
    internal static WorkerRequest Request(string action) => new()
    {
        Method = action, PortalProcessId = 42,
        MultiuserServerAlias = action == "list_server_connections" ? null : "Fixture",
        MultiuserGroupIsRoot = action is "list_server_projects" or "list_local_sessions" or "get_lock_state" ? true : null,
        MultiuserServerProjectName = action is "list_local_sessions" or "get_lock_state" ? "A" : null
    };

    internal static string Payload(string action)
    {
        var remote = new JsonObject
        {
            ["serverAlias"] = "Fixture", ["host"] = "server", ["port"] = 1234, ["protocol"] = null,
            ["group"] = action == "list_server_groups" ? null : new JsonObject { ["isRoot"] = true, ["name"] = null },
            ["serverProjectName"] = action is "list_local_sessions" or "get_lock_state" ? "A" : null,
            ["localSessionId"] = null, ["localSessionPath"] = null
        };
        var root = new JsonObject
        {
            ["remoteIdentity"] = remote,
            ["connectionObservation"] = new JsonObject { ["state"] = "connected", ["observedAt"] = "2026-10-05T12:00:00Z",
                ["observationSource"] = "explicitRead", ["previousState"] = null, ["transition"] = false }
        };
        switch (action)
        {
            case "list_server_connections": return """{"connections":[{"serverAlias":"Fixture","host":"server","port":1234}]}""";
            case "list_server_groups": root["groups"] = new JsonArray(new JsonObject { ["isRoot"] = false, ["name"] = "Group A" }); break;
            case "list_server_projects": root["projects"] = new JsonArray(new JsonObject { ["name"] = "A" }); break;
            case "list_local_sessions": root["scope"] = "currentMachineCurrentUser";
                root["sessions"] = new JsonArray(new JsonObject { ["sessionId"] = 0, ["projectPath"] = "C:\\Sessions\\A.als21" }); break;
            case "get_lock_state": root["isLocked"] = false; root["owner"] = null; root["observedAt"] = "2026-10-05T12:00:00Z"; break;
        }
        return root.ToJsonString();
    }

    [Theory]
    [InlineData("list_server_connections", "serverConnections")]
    [InlineData("list_server_groups", "serverGroups")]
    [InlineData("list_server_projects", "serverProjects")]
    [InlineData("list_local_sessions", "localSessions")]
    [InlineData("get_lock_state", "lockState")]
    public void Decode_ExactlyOneTypedSlotAndExplicitNulls(string action, string slot)
    {
        var outcome = ProjectBindingInspectionPayloadContract.Decode(Request(action), WorkerCallResult.Ok(Payload(action)) with { PortalProcessId = 42 });
        Assert.Null(outcome.Failure);
        var value = CanonicalJson.ToElement(outcome.Value);
        Assert.Equal(action, value.GetProperty("action").GetString());
        Assert.Equal(42, value.GetProperty("portalProcessId").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Object, value.GetProperty(slot).ValueKind);
        Assert.Equal(4, value.EnumerateObject().Count(p => p.Value.ValueKind == System.Text.Json.JsonValueKind.Null));
    }

    public static IEnumerable<object[]> CorruptPayloads()
    {
        foreach (var action in new[] { "list_server_connections", "list_server_groups", "list_server_projects", "list_local_sessions", "get_lock_state" })
        {
            yield return [action, "null"];
            yield return [action, "{}"];
            var root = JsonNode.Parse(Payload(action))!.AsObject();
            foreach (var key in root.Select(p => p.Key).ToArray())
            {
                var missing = root.DeepClone().AsObject(); missing.Remove(key); yield return [action, missing.ToJsonString()];
            }
            if (action != "get_lock_state")
            {
                var key = action switch { "list_server_connections" => "connections", "list_server_groups" => "groups", "list_server_projects" => "projects", _ => "sessions" };
                foreach (JsonNode? bad in new JsonNode?[] { null, new JsonArray((JsonNode?)null), JsonValue.Create("secret-sentinel") })
                { var copy = root.DeepClone(); copy[key] = bad; yield return [action, copy.ToJsonString()]; }
            }
            if (action != "list_server_connections")
            {
                foreach (var (key, value) in new[] { ("serverAlias", "secret-sentinel"), ("protocol", "invented"), ("host", "") })
                { var copy = root.DeepClone(); copy["remoteIdentity"]![key] = value; yield return [action, copy.ToJsonString()]; }
                foreach (var (key, value) in new[] { ("state", "secret-sentinel"), ("observationSource", "invented"), ("previousState", "invented") })
                { var copy = root.DeepClone(); copy["connectionObservation"]![key] = value; yield return [action, copy.ToJsonString()]; }
                var missing = root.DeepClone(); missing["remoteIdentity"]!.AsObject().Remove("localSessionId"); yield return [action, missing.ToJsonString()];
                var mismatch = root.DeepClone(); mismatch["remoteIdentity"]!["serverProjectName"] = "secret-sentinel"; yield return [action, mismatch.ToJsonString()];
            }
        }
        var sessions = JsonNode.Parse(Payload("list_local_sessions"))!;
        sessions["scope"] = "allUsers"; yield return ["list_local_sessions", sessions.ToJsonString()];
        sessions["scope"] = "currentMachineCurrentUser"; sessions["sessions"]![0]!["projectPath"] = "relative.als21"; yield return ["list_local_sessions", sessions.ToJsonString()];
        var locked = JsonNode.Parse(Payload("get_lock_state"))!; locked["isLocked"] = true; yield return ["get_lock_state", locked.ToJsonString()];
        locked["isLocked"] = false; locked["owner"] = "secret-sentinel"; yield return ["get_lock_state", locked.ToJsonString()];
    }

    [Theory, MemberData(nameof(CorruptPayloads))]
    public void Decode_RejectsMalformedOrIncoherentPayloadWithoutEcho(string action, string payload)
    {
        var result = ProjectBindingInspectionPayloadContract.Decode(Request(action), WorkerCallResult.Ok(payload) with { PortalProcessId = 42 });
        Assert.Equal("protocol_error", result.Failure?.Category); Assert.Null(result.Value);
        Assert.DoesNotContain("secret-sentinel", CanonicalJson.Serialize(result));
    }

    [Theory]
    [InlineData(null)] [InlineData(0)] [InlineData(-1)] [InlineData(43)]
    public void Decode_RequiresPositiveObservedMatchingPid(int? observed)
    {
        var result = ProjectBindingInspectionPayloadContract.Decode(Request("list_server_connections"), WorkerCallResult.Ok(Payload("list_server_connections")) with { PortalProcessId = observed });
        Assert.Equal("protocol_error", result.Failure?.Category); Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1)]
    public void Sessions_AcceptEveryIntegerIdentity(int sessionId)
    {
        var payload = JsonNode.Parse(Payload("list_local_sessions"))!; payload["sessions"]![0]!["sessionId"] = sessionId;
        var result = ProjectBindingInspectionPayloadContract.Decode(Request("list_local_sessions"), WorkerCallResult.Ok(payload.ToJsonString()) with { PortalProcessId = 42 });
        Assert.Null(result.Failure); Assert.Equal(sessionId, result.Value!.LocalSessions!.Sessions.Single().SessionId);
    }
}
