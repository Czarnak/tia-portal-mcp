using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Vci;

internal static class VciReadRoundtripScenario
{
    public static string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        var query = request.VciQuery!;
        if (query.WorkspaceName == "Missing")
            return JsonSerializer.Serialize(new WorkerResponse { Success = false, Error = "Synthetic workspace not found.", FailureCategory = "target_not_found" }, WorkerJson.Envelope);
        object value = request.Method switch
        {
            "vci_list_workspaces" => new VciWorkspaceListInfo { Workspaces = [new() { Name = "Fixture", RootPath = null, MappingCount = 3 }] },
            "vci_list_mappings" => new VciMappingListInfo
            {
                Total = 3, Offset = query.Offset ?? 0, Limit = query.Limit ?? 100,
                Mappings = [new() { FileKey = new("Scripts", "Fixture.xml"), Format = "SimaticML", Path = query.Scope,
                    Status = query.WithStatus ? new() { CompareState = "Equal", Details = null } : null }],
            },
            "vci_supported_formats" => new VciSupportedFormatsInfo { Formats = ["SimaticML", "SCL"] },
            _ => throw new InvalidOperationException("Unexpected VCI fixture method."),
        };
        var payload = query.WorkspaceName switch
        {
            "Malformed" => "{\"private\":\"PRIVATE_MARKER\"}",
            "MissingMember" => "{\"total\":0,\"offset\":0,\"limit\":100}",
            _ => WorkerJson.SerializePayload(value),
        };
        return JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload }, WorkerJson.Envelope);
    }
}
