using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Cursors;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Hmi;

/// <summary>
/// The only decoder of hmi_read worker payloads. Each operation declares exactly one result type in
/// <see cref="ResultTypes"/>; a payload that does not decode, or an operation without a declared type,
/// is a <c>protocol_error</c> and the payload is never echoed back.
/// </summary>
public static class HmiPayloadContract
{
    /// <summary>Operation name to its one result CLR type. Filled as each operation's reader lands.</summary>
    internal static readonly Dictionary<string, Type> ResultTypes = new(StringComparer.Ordinal)
    {
        ["list_hmi_devices"] = typeof(HmiDeviceListInfo),
        ["get_runtime_settings"] = typeof(HmiRuntimeSettingsInfo),
        ["list_script_modules"] = typeof(HmiScriptModuleListInfo),
        ["list_text_and_graphic_lists"] = typeof(HmiTextAndGraphicListsInfo),
        ["list_project_languages"] = typeof(HmiProjectLanguagesInfo),
        ["list_tag_tables"] = typeof(HmiTagTableTreeInfo),
        ["list_tags"] = typeof(HmiTagListInfo),
        ["get_tag"] = typeof(HmiTagDetailInfo),
        ["list_system_tags"] = typeof(HmiSystemTagListInfo),
        ["list_logging_tags"] = typeof(HmiLoggingTagListInfo),
        ["list_connections"] = typeof(HmiConnectionListInfo),
        ["list_alarms"] = typeof(HmiAlarmListInfo),
        ["get_alarm"] = typeof(HmiAlarmDetailInfo),
        ["list_alarm_classes"] = typeof(HmiAlarmClassesInfo),
        ["list_logs"] = typeof(HmiLogsInfo),
        ["list_screens"] = typeof(HmiScreenTreeInfo),
        ["list_screen_items"] = typeof(HmiScreenItemListInfo),
        ["list_faceplate_instances"] = typeof(HmiFaceplateInstanceListInfo),
        ["get_screen_navigation"] = typeof(HmiScreenNavigationInfo),
        ["validate"] = typeof(HmiValidationInfo),
    };

    private static readonly MethodInfo NormalizeAsMethod =
        typeof(HmiPayloadContract).GetMethod(nameof(NormalizeAs), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static StructuredOperationItem Project(HmiOperationRequest operation, WorkerCallResult workerResult)
        => Project(operation, workerResult, Console.Error.WriteLine);

    /// <summary>Projects a result and replaces a worker <c>page.snapshotId</c> with a sealed <c>page.nextCursor</c>.</summary>
    public static StructuredOperationItem Project(
        HmiOperationRequest operation,
        WorkerCallResult workerResult,
        HmiPageCursorCodec codec,
        ProjectBindingSnapshot binding)
        => Project(operation, workerResult, Console.Error.WriteLine, (codec, binding));

    internal static StructuredOperationItem Project(
        HmiOperationRequest operation,
        WorkerCallResult workerResult,
        Action<string> writeProtocolDiagnostic,
        (HmiPageCursorCodec Codec, ProjectBindingSnapshot Binding)? cursorIssuer = null)
    {
        var warnings = workerResult.Warnings ?? Array.Empty<string>();
        if (!workerResult.Success)
        {
            return Failed(
                operation,
                workerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                workerResult.Error ?? $"HMI operation '{operation.Operation}' failed.",
                warnings);
        }

        try
        {
            return new StructuredOperationItem(
                operation.OperationId,
                operation.Operation,
                OperationBatchStatus.Succeeded,
                IssueNextCursor(operation, Decode(operation, workerResult.Payload), cursorIssuer),
                Failure: null,
                Omission: null,
                SkipReason: null,
                warnings);
        }
        catch (Exception exception) when (exception is CursorException or JsonException)
        {
            // An unsealable worker snapshot id is a worker contract violation like any undecodable payload.
            // Server-side diagnostic only: names the operation, never the rejected payload.
            try
            {
                writeProtocolDiagnostic(
                    $"TiaMcpServer: worker payload contract rejection: operation={operation.Operation}.");
            }
            catch
            {
                // Diagnostics must never replace the stable fail-closed protocol_error response.
            }

            return Failed(
                operation,
                WorkerFailureCategories.ProtocolError,
                $"The worker payload for '{operation.Operation}' did not match its declared result "
                    + "contract and was rejected.",
                warnings);
        }
    }

    /// <exception cref="JsonException">No declared type, or the payload does not decode as it.</exception>
    private static JsonElement Decode(HmiOperationRequest operation, string payload)
    {
        if (!ResultTypes.TryGetValue(operation.Operation, out var type))
        {
            throw new JsonException($"No declared result contract for HMI operation '{operation.Operation}'.");
        }

        var normalize = (Func<string, JsonElement>)NormalizeAsMethod
            .MakeGenericMethod(type)
            .CreateDelegate(typeof(Func<string, JsonElement>));
        return normalize(payload);
    }

    /// <summary>
    /// The worker snapshot id never reaches the caller: it is removed, and while more rows exist it is
    /// sealed into <c>page.nextCursor</c> at <c>page.nextOffset</c>.
    /// </summary>
    private static JsonElement IssueNextCursor(
        HmiOperationRequest operation,
        JsonElement result,
        (HmiPageCursorCodec Codec, ProjectBindingSnapshot Binding)? cursorIssuer)
    {
        if (!result.TryGetProperty("page", out var page) || !page.TryGetProperty("snapshotId", out var snapshotId))
        {
            return result;
        }

        var root = JsonNode.Parse(result.GetRawText())!.AsObject();
        var pageNode = root["page"]!.AsObject();
        pageNode.Remove("snapshotId");
        if (cursorIssuer is { } issuer
            && snapshotId.ValueKind == JsonValueKind.String
            && page.GetProperty("nextOffset").ValueKind == JsonValueKind.Number)
        {
            pageNode["nextCursor"] = issuer.Codec.Encode(new HmiPageCursorState(
                snapshotId.GetString()!,
                operation.Operation,
                HmiPageCursorCodec.QueryHash(operation),
                page.GetProperty("nextOffset").GetInt32(),
                ProjectBindingCursorState.FromSnapshot(issuer.Binding)));
        }

        return JsonSerializer.SerializeToElement(root);
    }

    private static JsonElement NormalizeAs<T>(string payload)
        => CanonicalJson.NormalizeWorkerPayload<T>(payload).Element;

    private static StructuredOperationItem Failed(
        HmiOperationRequest operation,
        string category,
        string message,
        IReadOnlyList<string> warnings)
        => new(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Failed,
            Result: null,
            new StructuredOperationFailure(category, message),
            Omission: null,
            SkipReason: null,
            warnings);
}
