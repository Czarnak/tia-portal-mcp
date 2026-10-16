using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Vci;

internal sealed class VciWriteRoundtripScenario
{
    private readonly Dictionary<string, VciWorkspaceInfo> _workspaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _mutated = new(StringComparer.OrdinalIgnoreCase);
    public VciWriteRoundtripScenario()
    {
        foreach (var name in new[] { "Fixture", "Fail", "Blocked", "Healthy", "Other", "UnitMapping", "OuterFailure", "Unverifiable", "EmptyUnverifiable", "PartialCollision" })
            _workspaces[name] = new() { Name = name, RootPath = Path.Combine(Path.GetTempPath(), "vci-" + name) };
    }
    private static string Ok(object value) => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = WorkerJson.SerializePayload(value) }, WorkerJson.Envelope);
    private static string Fail(string category, VciWriteOutcomeInfo? value = null) => JsonSerializer.Serialize(new WorkerResponse
    { Success = false, FailureCategory = category, Error = "Synthetic VCI failure.", VciWriteOutcome = value }, WorkerJson.Envelope);

    public string Handle(string line, string scenario)
    {
        var r = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        if (r.Method == "read_hardware_config") return Ok(new HardwareConfigInfo());
        if (r.Method == "vci_list_workspaces") return Ok(new VciWorkspaceListInfo { Workspaces = _workspaces.Values.Select(w => new VciWorkspaceInfo
            { Name = w.Name, RootPath = w.RootPath, MappingCount = w.MappingCount + (w.Name == "UnitMapping" ? 1 : 0) }).ToList() });
        var w = r.VciWrite!;
        _workspaces.TryGetValue(w.WorkspaceName!, out var workspace);
        if (r.Method == "vci_plan")
        {
            if (w.Operation != "create_workspace" && workspace is null) return Fail("target_not_found");
            if (_mutated.Contains(w.WorkspaceName!) && w.WorkspaceName is "Unverifiable" or "EmptyUnverifiable")
                return Ok(new VciPlanInfo { Unverifiable = "Synthetic mapping inventory failed.", WorkspaceRootPath = workspace!.RootPath });
            return Ok(new VciPlanInfo { Leaves = w.Operation is "create_workspace" or "delete_workspace" || w.WorkspaceName == "EmptyUnverifiable" ? 0 : 2,
                MappedLeaves = workspace?.MappingCount ?? 0, WorkspaceRootPath = workspace?.RootPath ?? w.RootPath,
                WorkspaceConflict = w.Operation == "create_workspace" && (workspace is not null || _workspaces.Values.Any(x => string.Equals(x.RootPath, w.RootPath, StringComparison.OrdinalIgnoreCase))) ? "Workspace exists." : null,
                KeyCollisions = w.WorkspaceName == "Blocked" || w.WorkspaceName == "PartialCollision" && _mutated.Contains(w.WorkspaceName) ? ["Fixture/leaf"] : [] });
        }
        var outcome = new VciWriteOutcomeInfo { Leaves = w.Operation is "create_workspace" or "delete_workspace" ? 1 : 2 };
        if (w.WorkspaceName != "Healthy" && scenario is "vci-timeout" or "vci-timeout-silent" or "vci-crash")
        {
            if (scenario != "vci-timeout-silent") VciBreadcrumbFile.TryWrite(w.BreadcrumbPath!, new(w.RequestId!, w.OperationId!, w.Operation!, w.Scope!, null));
            if (scenario == "vci-crash") Environment.Exit(17);
            Thread.Sleep(Timeout.Infinite);
        }
        if (w.WorkspaceName != "Healthy" && scenario == "vci-portal-lost")
        {
            if (w.WorkspaceName == "OuterFailure") return Fail("portal_lost", new() { Leaves = 0, Failed = 1 });
            outcome.LastAttempted = new() { Path = w.Scope!, Operation = w.Operation!, Format = null };
            return Fail("portal_lost", outcome);
        }
        switch (w.Operation)
        {
            case "create_workspace":
                if (workspace is not null || _workspaces.Values.Any(x => string.Equals(x.RootPath, w.RootPath, StringComparison.OrdinalIgnoreCase))) return Fail("state_changed");
                _workspaces[w.WorkspaceName!] = new() { Name = w.WorkspaceName!, RootPath = w.RootPath }; outcome.Succeeded = 1; outcome.ProjectModified = true; break;
            case "delete_workspace": _workspaces.Remove(w.WorkspaceName!); outcome.Succeeded = 1; outcome.ProjectModified = true; break;
            case "export":
                outcome.Skipped.AlreadyMapped = workspace!.MappingCount;
                if (w.WorkspaceName == "EmptyUnverifiable") outcome.Leaves = 0;
                outcome.Failed = w.WorkspaceName is "Fail" or "PartialCollision" ? 1 : 0;
                outcome.Succeeded = outcome.Leaves - outcome.Skipped.AlreadyMapped - outcome.Failed;
                workspace.MappingCount += outcome.Succeeded; outcome.ProjectModified = outcome.Succeeded > 0; break;
            case "delete_mapping": outcome.Leaves = outcome.Succeeded = workspace!.MappingCount; workspace.MappingCount = 0; outcome.ProjectModified = outcome.Succeeded > 0; break;
            case "sync_to_workspace": case "sync_to_project": outcome.Skipped.Equal = 2; break;
        }
        _mutated.Add(w.WorkspaceName!);
        return outcome.Failed > 0 ? Fail("worker_operation_failed", outcome) : Ok(outcome);
    }
}
