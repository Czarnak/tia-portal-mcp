using System.Text.Json;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Vci;

public sealed class VciWriteVerifier(OpennessWorkerClient client, IReadOnlyList<VciOperationRequest> items)
{
    private static readonly HashSet<string> NeverRan = new(StringComparer.Ordinal)
    { "validation_error", "target_not_found", "target_ambiguous", "state_changed", "guard_blocked", "binding_conflict", "access_denied" };

    public async Task<VciWriteVerification> VerifyAsync(string? projectPath, StructuredOperationBatch batch)
    {
        var results = new List<VciOperationVerification>();
        var fatal = batch.Operations.Any(o => o.Failure?.Category is "portal_lost" or "worker_timeout" or "worker_crashed");
        var outcomes = batch.Operations.ToDictionary(o => o.OperationId, StringComparer.Ordinal);
        var attempted = items.Where(i => outcomes.TryGetValue(i.OperationId, out var o)
            && o.Status is OperationBatchStatus.Succeeded or OperationBatchStatus.Failed
            && !NeverRan.Contains(o.Failure?.Category ?? "")).ToArray();
        foreach (var item in items)
        {
            if (!outcomes.TryGetValue(item.OperationId, out var outcome)) continue;
            void Check(bool success, string check, string? expected = null, string? actual = null, string? message = null)
                => results.Add(new(item.OperationId, success, check, expected, actual, message));
            if (fatal || outcome.Status == OperationBatchStatus.Skipped || NeverRan.Contains(outcome.Failure?.Category ?? ""))
            { Check(false, "notVerified", message: "No verification read was made."); continue; }
            if (attempted.Last(i => string.Equals(i.WorkspaceName, item.WorkspaceName, StringComparison.OrdinalIgnoreCase)) != item)
            { Check(true, "superseded"); continue; }
            if (item.Operation is "sync_to_workspace" or "sync_to_project")
            { Check(true, "notStatusVerified", message: "Status was not re-read; use vci_read list_mappings withStatus for status counts."); continue; }
            try
            {
                if (item.Operation is "create_workspace" or "delete_workspace")
                {
                    var read = await client.ReadVciAsync(VciMethods.ForOperation("list_workspaces"), new(), projectPath).ConfigureAwait(false);
                    if (!read.Success) { Check(false, "notVerified", message: read.Error); continue; }
                    var list = CanonicalJson.DeserializeWorkerPayload<VciWorkspaceListInfo>(read.Payload);
                    if (list.Workspaces is null || list.Workspaces.Any(w => w is null || string.IsNullOrWhiteSpace(w.Name))) throw new JsonException();
                    var found = list.Workspaces.Where(w => string.Equals(w.Name, item.WorkspaceName, StringComparison.OrdinalIgnoreCase)).ToArray();
                    var success = item.Operation == "delete_workspace" ? found.Length == 0 : found.Length == 1
                        && string.Equals(Path.GetFullPath(found[0].RootPath ?? "").TrimEnd('\\'), Path.GetFullPath(item.RootPath!).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                    Check(success, item.Operation == "delete_workspace" ? "absent" : "exists");
                }
                else
                {
                    var plan = await new VciWritePlanner(client).ReplanAsync(projectPath, item).ConfigureAwait(false);
                    if (!plan.Replan.Success) { Check(false, "notVerified", message: plan.Replan.Error!.Message); continue; }
                    if (plan.Guards.FirstOrDefault(g => g.Id == VciGuardDefinitions.StateUnverifiable) is { } unverifiable)
                    { Check(false, "notVerified", message: unverifiable.Message); continue; }
                    var effect = plan.Replan.Plan!.Effect!;
                    var value = outcome.Result is { } payload ? CanonicalJson.DeserializeWorkerPayload<VciWriteOutcomeInfo>(payload.GetRawText()) : null;
                    if (value is null) { Check(false, "notVerified", message: "No trusted write counts are available."); continue; }
                    var expected = item.Operation == "delete_mapping" ? 0 : value.Leaves - value.Failed;
                    Check(effect.MappedLeaves == expected && (item.Operation == "delete_mapping" || effect.Leaves == value.Leaves),
                        "mappedLeaves", expected.ToString(), effect.MappedLeaves.ToString());
                }
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            { Check(false, "notVerified", message: "VCI verification evidence did not match its declared contract."); }
        }
        return new(results.All(r => r.Success), results, null);
    }
}
