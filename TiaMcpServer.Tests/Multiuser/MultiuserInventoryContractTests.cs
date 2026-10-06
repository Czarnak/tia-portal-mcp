using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

public sealed class MultiuserInventoryContractTests
{
    [Fact]
    public void InventoryPayloads_WriteRequiredMembersAndRejectMissingMembers()
    {
        AssertRequiredMembers(new MultiuserServerConnectionsInfo
        {
            Connections = [new MultiuserServerConnectionInfo { ServerAlias = " Fixture ", Host = "server", Port = 8735 }],
        }, "connections");
        AssertRequiredMembers(new MultiuserServerGroupsInfo
        {
            Groups = [new ProjectServerGroupIdentity { IsRoot = true }],
        }, "connectionObservation", "groups", "remoteIdentity");
        AssertRequiredMembers(new MultiuserServerProjectsInfo
        {
            Projects = [new MultiuserServerProjectInfo { Name = "Line1" }],
        }, "connectionObservation", "projects", "remoteIdentity");
        AssertRequiredMembers(new MultiuserLocalSessionsInfo
        {
            Sessions = [new MultiuserLocalSessionInfo { SessionId = 42, ProjectPath = @"C:\Sessions\Line1.als21" }],
        }, "connectionObservation", "remoteIdentity", "scope", "sessions");
        AssertRequiredMembers(new MultiuserLockStateInfo(),
            "connectionObservation", "isLocked", "observedAt", "owner", "remoteIdentity");
    }

    [Fact]
    public void Inventory_DescribesExactGroupAndProjectWithoutInventingProtocolOrSession()
    {
        var inventory = new MultiuserServerProjectsInfo
        {
            RemoteIdentity = new MultiuserRemoteIdentity
            {
                ServerAlias = " Fixture ",
                Group = new ProjectServerGroupIdentity { IsRoot = false, Name = " Team " },
                ServerProjectName = "Line1",
            },
        };
        var normalized = CanonicalJson.NormalizeWorkerPayload<MultiuserServerProjectsInfo>(WorkerJson.SerializePayload(inventory));
        var identity = normalized.Element.GetProperty("remoteIdentity");
        Assert.Equal(" Fixture ", identity.GetProperty("serverAlias").GetString());
        Assert.Equal(" Team ", identity.GetProperty("group").GetProperty("name").GetString());
        Assert.False(identity.GetProperty("group").GetProperty("isRoot").GetBoolean());
        Assert.Equal("Line1", identity.GetProperty("serverProjectName").GetString());
        foreach (var name in new[] { "protocol", "localSessionId", "localSessionPath" })
            Assert.Equal(JsonValueKind.Null, identity.GetProperty(name).ValueKind);
    }

    [Fact]
    public void EmptySessionInventory_RetainsCurrentMachineCurrentUserScope()
    {
        using var document = JsonDocument.Parse(WorkerJson.SerializePayload(new MultiuserLocalSessionsInfo()));
        Assert.Equal("currentMachineCurrentUser", document.RootElement.GetProperty("scope").GetString());
        Assert.Empty(document.RootElement.GetProperty("sessions").EnumerateArray());
    }

    [Fact]
    public void UnlockedState_WritesNullOwnerAndExplicitObservationTime()
    {
        var observedAt = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        using var document = JsonDocument.Parse(WorkerJson.SerializePayload(new MultiuserLockStateInfo { ObservedAt = observedAt }));
        Assert.False(document.RootElement.GetProperty("isLocked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("owner").ValueKind);
        Assert.Equal(observedAt, document.RootElement.GetProperty("observedAt").GetDateTimeOffset());
    }

    [Fact]
    public void FlatSelectors_RoundTripWithoutRequiringProjectBinding()
    {
        var request = new WorkerRequest
        {
            Method = "list_server_projects", PortalProcessId = 1234, MultiuserServerAlias = "Fixture",
            MultiuserGroupIsRoot = true, MultiuserGroupName = null, MultiuserServerProjectName = null,
        };
        var wire = JsonSerializer.Serialize(request, WorkerJson.Request);
        var decoded = JsonSerializer.Deserialize<WorkerRequest>(wire, WorkerJson.Envelope)!;
        Assert.Equal(1234, decoded.PortalProcessId);
        Assert.Equal("Fixture", decoded.MultiuserServerAlias);
        Assert.True(decoded.MultiuserGroupIsRoot);
        Assert.Null(decoded.MultiuserGroupName);
        Assert.Null(decoded.MultiuserServerProjectName);
        Assert.Null(decoded.ProjectPath);
        Assert.Null(decoded.ExpectedSessionIdentity);
    }

    [Fact]
    public void ResponseAttachmentEvidence_DoesNotInventProjectBinding()
    {
        var wire = JsonSerializer.Serialize(new WorkerResponse { Success = true, PortalProcessId = 1234 }, WorkerJson.Envelope);
        using var document = JsonDocument.Parse(wire);
        Assert.Equal(1234, document.RootElement.GetProperty("portalProcessId").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("sessionIdentity", out _));
        Assert.Equal(1234, JsonSerializer.Deserialize<WorkerResponse>(wire, WorkerJson.Envelope)!.PortalProcessId);
        Assert.DoesNotContain("portalProcessId", JsonSerializer.Serialize(new WorkerResponse(), WorkerJson.Envelope));
    }

    [Fact]
    public void Requests_HaveDistinctSiemensFreeContracts()
    {
        Type[] requests = [typeof(MultiuserServerConnectionsRequest), typeof(MultiuserServerGroupsRequest),
            typeof(MultiuserServerProjectsRequest), typeof(MultiuserLocalSessionsRequest), typeof(MultiuserLockStateRequest)];
        Assert.Equal(5, requests.Distinct().Count());
        Assert.All(requests, type => Assert.Equal(typeof(int?), type.GetProperty("PortalProcessId")!.PropertyType));
        Assert.DoesNotContain(typeof(MultiuserServerConnectionsInfo).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Siemens.Engineering", StringComparison.Ordinal) == true);
    }

    private static void AssertRequiredMembers<T>(T value, params string[] names)
    {
        var wire = WorkerJson.SerializePayload(value);
        var document = JsonNode.Parse(wire)!.AsObject();
        Assert.Equal(names, document.Select(property => property.Key).Order(StringComparer.Ordinal).ToArray());
        CanonicalJson.NormalizeWorkerPayload<T>(wire);
        AssertMissingRejected<T>(document, document);
    }

    private static void AssertMissingRejected<T>(JsonObject root, JsonNode current)
    {
        if (current is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                obj.Remove(property.Key);
                Assert.Throws<JsonException>(() => CanonicalJson.NormalizeWorkerPayload<T>(root.ToJsonString()));
                obj.Add(property.Key, property.Value);
                if (property.Value is not null) AssertMissingRejected<T>(root, property.Value);
            }
        }
        else if (current is JsonArray array)
            foreach (var item in array) if (item is not null) AssertMissingRejected<T>(root, item);
    }
}
