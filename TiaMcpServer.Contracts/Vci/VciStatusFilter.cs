namespace TiaMcpServer.Contracts.Vci;

public enum VciSyncDirection { ToWorkspace, ToProject }
public enum VciSyncDecision { Sync, SkipEqual, SkipTargetChanged, SkipConflict, FailMissing }

public static class VciStatusFilter
{
    public static VciSyncDecision Decide(VciSyncDirection direction, string compareState, string details)
    {
        if (direction != VciSyncDirection.ToWorkspace && direction != VciSyncDirection.ToProject)
            return VciSyncDecision.SkipConflict;
        if (compareState == "Equal")
            return VciSyncDecision.SkipEqual;
        if (compareState == "WorkspaceFileMissing")
            return direction == VciSyncDirection.ToProject ? VciSyncDecision.FailMissing : VciSyncDecision.SkipTargetChanged;
        if (compareState != "Unequal")
            return VciSyncDecision.SkipConflict;

        var projectChanged = false;
        var workspaceChanged = false;
        foreach (var flag in details.Split(','))
        {
            switch (flag.Trim())
            {
                case "ProjectObjectChanged": projectChanged = true; break;
                case "WorkspaceFileChanged": workspaceChanged = true; break;
                case "None": break;
                default: return VciSyncDecision.SkipConflict;
            }
        }

        if (projectChanged == workspaceChanged)
            return VciSyncDecision.SkipConflict;
        return projectChanged == (direction == VciSyncDirection.ToWorkspace)
            ? VciSyncDecision.Sync : VciSyncDecision.SkipTargetChanged;
    }
}
