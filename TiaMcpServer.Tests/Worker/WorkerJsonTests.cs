using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// WorkerJson is the only worker-wire serializer policy. Payloads write null members unless a
/// member is explicitly conditional; the envelope reads case-insensitively and omits null members.
/// </summary>
public sealed class WorkerJsonTests
{
    [Fact]
    public void SerializePayload_WritesNullMembers()
    {
        var json = WorkerJson.SerializePayload(new Payload { Name = "a" });

        Assert.Equal("""{"name":"a","optional":null}""", json);
    }

    [Fact]
    public void SerializePayload_WritesNullMembersInArraysAndLists()
    {
        Assert.Equal(
            """[{"name":"a","optional":null}]""",
            WorkerJson.SerializePayload(new List<Payload> { new() { Name = "a" } }));
        Assert.Equal(
            """[{"name":"a","optional":null}]""",
            WorkerJson.SerializePayload(new[] { new Payload { Name = "a" } }));
    }

    [Fact]
    public void SerializePayload_UsesTheRuntimeTypeOfTheRoot()
    {
        object payload = new Payload { Name = "a" };

        Assert.Equal("""{"name":"a","optional":null}""", WorkerJson.SerializePayload(payload));
    }

    [Fact]
    public void Envelope_ReadsCaseInsensitivelyAndOmitsNullMembersOnWrite()
    {
        var response = JsonSerializer.Deserialize<WorkerResponse>(
            """{"Success":true,"PAYLOAD":"{}"}""",
            WorkerJson.Envelope)!;

        Assert.True(response.Success);
        Assert.Equal("{}", response.Payload);
        Assert.Equal(
            """{"success":true,"payload":"{}"}""",
            JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = "{}" }, WorkerJson.Envelope));
    }

    [Fact]
    public void Request_WritesNullMembers()
    {
        var json = JsonSerializer.Serialize(new WorkerRequest { Method = "get_project_status" }, WorkerJson.Request);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("get_project_status", document.RootElement.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("projectPath").ValueKind);
    }

    [Fact]
    public void Request_OmitsRetiredConfirmMember()
    {
        var json = JsonSerializer.Serialize(new WorkerRequest { Method = "save_project" }, WorkerJson.Request);

        using var document = JsonDocument.Parse(json);
        Assert.DoesNotContain(document.RootElement.EnumerateObject(),
            property => property.Name.Equals("confirm", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Payload
    {
        public string Name { get; set; } = string.Empty;

        public string? Optional { get; set; }
    }
}
