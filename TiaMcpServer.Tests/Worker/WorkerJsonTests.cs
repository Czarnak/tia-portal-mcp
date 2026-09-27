using System.Text.Json;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// WorkerJson is the only worker-wire serializer policy. A payload contract writes null members
/// unless it carries <see cref="LegacyNullOmissionAttribute"/>; a collection payload follows its
/// element contract; the envelope reads case-insensitively and omits null members.
/// </summary>
public sealed class WorkerJsonTests
{
    [Fact]
    public void SerializePayload_WritesNullMembersOfAnUnmarkedContract()
    {
        var json = WorkerJson.SerializePayload(new UnmarkedPayload { Name = "a" });

        Assert.Equal("""{"name":"a","optional":null}""", json);
    }

    [Fact]
    public void SerializePayload_OmitsNullMembersOfAMarkedContract()
    {
        var json = WorkerJson.SerializePayload(new MarkedPayload { Name = "a" });

        Assert.Equal("""{"name":"a"}""", json);
    }

    [Fact]
    public void SerializePayload_AppliesTheElementPolicyToCollections()
    {
        Assert.Equal(
            """[{"name":"a"}]""",
            WorkerJson.SerializePayload(new List<MarkedPayload> { new() { Name = "a" } }));
        Assert.Equal(
            """[{"name":"a","optional":null}]""",
            WorkerJson.SerializePayload(new[] { new UnmarkedPayload { Name = "a" } }));
    }

    [Fact]
    public void SerializePayload_UsesTheRuntimeTypeOfTheRoot()
    {
        object payload = new MarkedPayload { Name = "a" };

        Assert.Equal("""{"name":"a"}""", WorkerJson.SerializePayload(payload));
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

    private sealed class UnmarkedPayload
    {
        public string Name { get; set; } = string.Empty;

        public string? Optional { get; set; }
    }

    [LegacyNullOmission(LegacyNullOmissionReason.BatchRedesign)]
    private sealed class MarkedPayload
    {
        public string Name { get; set; } = string.Empty;

        public string? Optional { get; set; }
    }
}
