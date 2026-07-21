using System;
using System.IO;

namespace TiaMcpServer.Contracts;

public enum ProjectOpenDecision
{
    /// <summary>Operate on whatever is already attached.</summary>
    UseAttached,

    /// <summary>Nothing is attached; opening the requested project cannot clobber anything.</summary>
    OpenRequested,

    /// <summary>A different project is attached; refuse rather than open one alongside it.</summary>
    Refuse
}

/// <summary>
/// Decides whether a non-lifecycle operation may cause TIA Portal to open a project. Read
/// operations must never open a second project alongside one the user already has open — live
/// testing against V21 showed a read tool doing exactly that, stopped only by TIA Portal's own
/// refusal. Pure so the net8.0 test project can cover it; the worker is net48 and references
/// Siemens assemblies the tests cannot load.
/// </summary>
public static class ProjectOpenPolicy
{
    public static ProjectOpenDecision Decide(string? currentPath, string? requestedPath)
    {
        var requested = Normalize(requestedPath);
        if (requested is null)
        {
            return ProjectOpenDecision.UseAttached;
        }

        var current = Normalize(currentPath);
        if (current is null)
        {
            return ProjectOpenDecision.OpenRequested;
        }

        return string.Equals(current, requested, StringComparison.OrdinalIgnoreCase)
            ? ProjectOpenDecision.UseAttached
            : ProjectOpenDecision.Refuse;
    }

    public static string RefusalMessage(string currentPath, string requestedPath)
        => $"TIA Portal currently has project '{currentPath}' open, but this request targets "
            + $"'{requestedPath}'. Read operations never switch projects. Omit projectPath to use "
            + "the open project, or call open_project to switch.";

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path!.Trim();
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Not a resolvable path (a FakeWorker scenario keyword, for instance). Compare literally.
            return trimmed;
        }
    }
}
