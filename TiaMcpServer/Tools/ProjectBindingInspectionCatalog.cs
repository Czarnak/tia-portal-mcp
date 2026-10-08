using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.Tools;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MultiuserGroupSelector
{
    [JsonRequired] public bool? IsRoot { get; set; }
    [JsonRequired] public string? Name { get; set; }
}

internal static class ProjectBindingInspectionCatalog
{
    internal const string InvalidArguments = "Invalid bind_project action or selectors. Use bind with an optional .ap21/.amc21 projectPath, forceRebind and positive portalProcessId; list_portals without selectors; or an inventory action with its exact Portal/server/group/project selectors.";

    internal static bool IsInventory(string action) => action is "list_server_connections" or "list_server_groups"
        or "list_server_projects" or "list_local_sessions" or "get_lock_state";

    internal static bool ValidArguments(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null) return true;
        var action = "bind";
        if (arguments.TryGetValue("action", out var actionValue) && actionValue.ValueKind != JsonValueKind.Null)
        {
            if (actionValue.ValueKind != JsonValueKind.String) return false;
            action = actionValue.GetString()!;
        }
        if (action == "bind")
        {
            if (arguments.Keys.Any(key => key is not ("action" or "projectPath" or "forceRebind" or "portalProcessId"))) return false;
            if (arguments.TryGetValue("forceRebind", out var force)
                && force.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)) return false;
            if (arguments.TryGetValue("portalProcessId", out var bindProcess)
                && (bindProcess.ValueKind != JsonValueKind.Number || !bindProcess.TryGetInt32(out var id) || id <= 0)) return false;
            if (!arguments.TryGetValue("projectPath", out var path) || path.ValueKind == JsonValueKind.Null) return true;
            return path.ValueKind == JsonValueKind.String && Path.IsPathFullyQualified(path.GetString()!)
                && Path.GetExtension(path.GetString()) is { } extension
                && (string.Equals(extension, ".ap21", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".amc21", StringComparison.OrdinalIgnoreCase));
        }
        if (action == "list_portals") return arguments.Keys.All(key => key == "action");
        if (!IsInventory(action)) return false;
        var needsAlias = action != "list_server_connections";
        var needsGroup = action is "list_server_projects" or "list_local_sessions" or "get_lock_state";
        var needsProject = action is "list_local_sessions" or "get_lock_state";
        if (arguments.Keys.Any(key => key != "action" && key != "portalProcessId"
            && !(needsAlias && key == "serverAlias") && !(needsGroup && key == "group")
            && !(needsProject && key == "serverProjectName"))) return false;
        int? pid = null;
        if (arguments.TryGetValue("portalProcessId", out var process))
        {
            if (process.ValueKind != JsonValueKind.Number || !process.TryGetInt32(out var id) || id <= 0) return false;
            pid = id;
        }
        string? Name(string key) => arguments.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        MultiuserGroupSelector? group = null;
        if (needsGroup)
        {
            if (!arguments.TryGetValue("group", out var value)) return false;
            try { group = CanonicalJson.Deserialize<MultiuserGroupSelector>(value.GetRawText()); }
            catch (JsonException) { return false; }
        }
        return TryCreate(action, pid, Name("serverAlias"), group, Name("serverProjectName"), out _);
    }

    internal static bool TryCreate(string action, int? pid, string? alias, MultiuserGroupSelector? group,
        string? project, out WorkerRequest request)
    {
        request = new WorkerRequest { Method = action == "list_portals" ? "list_tia_portal_processes" : action };
        if (action == "list_portals") return pid is null && alias is null && group is null && project is null;
        if (!IsInventory(action) || pid <= 0) return false;
        var needsAlias = action != "list_server_connections";
        var needsGroup = action is "list_server_projects" or "list_local_sessions" or "get_lock_state";
        var needsProject = action is "list_local_sessions" or "get_lock_state";
        if (needsAlias ? string.IsNullOrWhiteSpace(alias) : alias is not null) return false;
        if (needsProject ? string.IsNullOrWhiteSpace(project) : project is not null) return false;
        if (needsGroup ? group?.IsRoot is null || (group.IsRoot.Value ? group.Name is not null : string.IsNullOrWhiteSpace(group.Name)) : group is not null) return false;
        request.PortalProcessId = pid;
        request.MultiuserServerAlias = alias;
        request.MultiuserGroupIsRoot = group?.IsRoot;
        request.MultiuserGroupName = group?.Name;
        request.MultiuserServerProjectName = project;
        return true;
    }
}
