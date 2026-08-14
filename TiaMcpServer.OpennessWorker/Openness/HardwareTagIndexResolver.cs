using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Resolves the single PLC whose tag tables are matched, deterministically and without a
/// first-match fallback: an exact (ordinal) <c>plcName</c> match against the PLC software name
/// or its owning device name, or — when omitted — the only PLC in the project.
/// </summary>
public static class HardwareTagIndexResolver
{
    public static IoTagIndex? Resolve(
        Project project,
        string? plcName,
        List<string> messages)
    {
        var discovered = PlcSoftwareLocator.FindAll(project, plcName: null).ToList();

        PlcSoftwareLocator.DiscoveredPlcSoftware? selected;
        if (plcName is not null)
        {
            var exact = discovered
                .Where(software =>
                    string.Equals(software.Software.Name, plcName, StringComparison.Ordinal)
                    || string.Equals(software.DeviceName, plcName, StringComparison.Ordinal))
                .ToList();
            if (exact.Count == 1)
            {
                selected = exact[0];
            }
            else
            {
                messages.Add(exact.Count == 0
                    ? $"No PLC named '{plcName}' was found; no tag matches are reported."
                    : $"More than one PLC matches '{plcName}'; no tag matches are reported because the PLC selection is ambiguous.");
                return null;
            }
        }
        else if (discovered.Count == 1)
        {
            selected = discovered[0];
        }
        else
        {
            messages.Add(discovered.Count == 0
                ? "No PLC software was found in the project; no tag matches are reported."
                : "More than one PLC exists and no plcName was supplied; no tag matches are reported. Supply plcName to select one PLC.");
            return null;
        }

        var tables = TagTableReader.ReadAll(selected.Software);
        var candidates = tables
            .SelectMany(table => table.Tags.Select(tag => new IoTagCandidate(
                tag.Name,
                tag.DataType,
                tag.LogicalAddress,
                table.Name,
                table.FolderPath)))
            .ToList();

        return new IoTagIndex(selected.DeviceName, candidates);
    }
}
