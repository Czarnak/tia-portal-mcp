using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Vci;

public static class VciPayloadContract
{
    internal static readonly Dictionary<string, Type> ResultTypes = new(StringComparer.Ordinal)
    {
        ["list_workspaces"] = typeof(VciWorkspaceListInfo),
        ["list_mappings"] = typeof(VciMappingListInfo),
        ["supported_formats"] = typeof(VciSupportedFormatsInfo),
    };
    private static readonly MethodInfo NormalizeMethod = typeof(VciPayloadContract).GetMethod(nameof(Normalize), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static StructuredOperationItem Project(VciOperationRequest item, WorkerCallResult result)
        => Project(item, result, Console.Error.WriteLine);

    internal static StructuredOperationItem Project(
        VciOperationRequest item,
        WorkerCallResult result,
        Action<string> writeDiagnostic)
    {
        var warnings = result.Warnings ?? Array.Empty<string>();
        if (!result.Success)
            return StructuredOperationItem.Failed(item, result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed, result.Error ?? "VCI read failed.", warnings);
        try
        {
            if (!ResultTypes.TryGetValue(item.Operation, out var type)) throw new JsonException();
            var normalize = (Func<string, JsonElement>)NormalizeMethod.MakeGenericMethod(type).CreateDelegate(typeof(Func<string, JsonElement>));
            return new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, normalize(result.Payload), null, null, null, warnings);
        }
        catch (JsonException)
        {
            StructuredPayloadProjection.TryWriteProtocolDiagnostic(item.Operation, writeDiagnostic);
            return StructuredOperationItem.Failed(item, WorkerFailureCategories.ProtocolError, $"The worker payload for '{item.Operation}' did not match its declared result contract and was rejected.", warnings);
        }
    }

    private static JsonElement Normalize<T>(string payload) => CanonicalJson.NormalizeWorkerPayload<T>(payload).Element;
}
