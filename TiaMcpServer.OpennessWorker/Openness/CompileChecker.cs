using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class CompileChecker
{
    public static CompileCheckReport Compile(Project project, string? plcName, string? blockPath)
    {
        var report = !string.IsNullOrWhiteSpace(blockPath)
            ? CompileBlock(project, plcName, blockPath!)
            : CompilePlcSoftware(project, plcName);
        CompileReportProjection.BoundSerializedReport(report);
        return report;
    }

    private static CompileCheckReport CompileBlock(Project project, string? plcName, string blockPath)
    {
        var address = BlockAddress.Parse(blockPath);
        if (address.PlcName == null && !string.IsNullOrWhiteSpace(plcName))
        {
            address = BlockAddress.Parse(plcName + "/" + blockPath);
        }

        var selectedPlc = PlcSoftwareLocator.FindAll(project, address.PlcName).FirstOrDefault()
            ?? throw new InvalidOperationException("No matching PLC software was found in the project.");
        var target = BlockTargetResolver.ResolveForExport(selectedPlc.Software, address);

        if (target.Block == null)
        {
            throw new InvalidOperationException($"Block '{address.BlockName}' not found.");
        }

        var result = CompileObject(target.Block);
        var plc = BuildPlcCompileInfo(selectedPlc, result, new CompileReportProjection.Budget());
        if (address.PlcName == null)
        {
            plc.DiagnosticNotes.Add("No PLC qualifier was specified; compiled using the first PLC found.");
        }

        var report = new CompileCheckReport
        {
            Scope = "block",
            BlockPath = blockPath,
            TotalErrorCount = plc.ErrorCount,
            TotalWarningCount = plc.WarningCount,
            OverallState = plc.State
        };

        report.Plcs.Add(plc);
        return report;
    }

    private static CompileCheckReport CompilePlcSoftware(Project project, string? plcName)
    {
        var report = new CompileCheckReport
        {
            Scope = "plc",
            OverallState = "Success"
        };
        var budget = new CompileReportProjection.Budget();

        foreach (var plc in PlcSoftwareLocator.FindAll(project, plcName))
        {
            try
            {
                var result = CompileObject(plc.Software);
                report.Plcs.Add(BuildPlcCompileInfo(plc, result, budget));
            }
            catch (EngineeringException)
            {
                report.Plcs.Add(BuildPlcCompileInfo(plc, null, budget));
            }
        }

        if (report.Plcs.Count == 0)
        {
            var detail = plcName is null ? string.Empty : $" named '{plcName}'";
            throw new InvalidOperationException($"No PLC software{detail} was found in the project.");
        }

        foreach (var plc in report.Plcs)
        {
            report.TotalErrorCount += plc.ErrorCount;
            report.TotalWarningCount += plc.WarningCount;
            report.OverallState = WorstState(report.OverallState, plc.State);
        }

        return report;
    }

    private static PlcCompileInfo BuildPlcCompileInfo(PlcSoftwareLocator.DiscoveredPlcSoftware selectedPlc,
        CompilerResult? result, CompileReportProjection.Budget budget)
    {
        var plc = new PlcCompileInfo
        {
            PlcName = selectedPlc.Software.Name,
            DeviceName = selectedPlc.DeviceName,
            State = result == null ? "Error" : MapState(result.State),
            ErrorCount = result?.ErrorCount ?? 0,
            WarningCount = result?.WarningCount ?? 0
        };
        if (result == null)
        {
            plc.DiagnosticNotes.Add("PLC compilation failed; compiler details are unavailable.");
            return plc;
        }

        var projection = CompileReportProjection.Flatten(result.Messages,
            message => message.Description,
            ReadMessagePath,
            MapMessageSeverity,
            message => message.Messages,
            budget);
        plc.Messages = projection.Messages;
        if (projection.WasTruncated)
            CompileReportProjection.NoteOmission(plc);
        return plc;
    }

    private static CompilerResult CompileObject(object compilable)
    {
        // Most compilable Openness objects (e.g. PlcSoftware) do not implement
        // ICompilable directly - the capability is exposed as a service via
        // IEngineeringServiceProvider.GetService<T>(), not as a type-level
        // interface, so reflecting over GetInterfaces() never finds it.
        if (compilable is IEngineeringServiceProvider serviceProvider)
        {
            var compilableService = serviceProvider.GetService<ICompilable>();
            if (compilableService != null)
            {
                return compilableService.Compile();
            }
        }

        var compileMethod = FindCompileMethod(compilable.GetType());
        if (compileMethod == null)
        {
            throw new InvalidOperationException($"Object '{compilable.GetType().Name}' does not expose a Compile method.");
        }

        try
        {
            return (CompilerResult)compileMethod.Invoke(compilable, null)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static MethodInfo? FindCompileMethod(Type type)
    {
        var compileMethod = type.GetMethod("Compile", BindingFlags.Instance | BindingFlags.Public);
        if (compileMethod != null)
        {
            return compileMethod;
        }

        foreach (var interfaceType in type.GetInterfaces())
        {
            compileMethod = interfaceType.GetMethod("Compile", BindingFlags.Instance | BindingFlags.Public);
            if (compileMethod != null)
            {
                return compileMethod;
            }
        }

        return null;
    }

    private static string MapState(CompilerResultState state)
    {
        switch (state)
        {
            case CompilerResultState.Success:
                return "Success";
            case CompilerResultState.Warning:
                return "Warning";
            case CompilerResultState.Error:
                return "Error";
            default:
                return state.ToString();
        }
    }

    private static string MapMessageSeverity(CompilerResultMessage message)
    {
        // Counts may include child messages. The row's own State is authoritative.
        // Resolve reflectively because the compile-time stubs may omit this property.
        var property = message.GetType().GetProperty("State")
            ?? throw new InvalidOperationException("Compiler message state is unavailable.");
        var state = property.GetValue(message, null)?.ToString();
        return state == "Error" ? "Error" : state == "Warning" ? "Warning" : "Information";
    }

    private static string ReadMessagePath(CompilerResultMessage message)
    {
        // Path is not declared on the compile-time Openness stub; resolved at runtime from the full V21 assembly.
        var property = message.GetType().GetProperty("Path")
            ?? throw new InvalidOperationException("Compiler message path is unavailable.");
        return property.GetValue(message, null)?.ToString() ?? string.Empty;
    }

    private static string WorstState(string current, string candidate)
    {
        if (current == "Error" || candidate == "Error")
        {
            return "Error";
        }

        if (current == "Warning" || candidate == "Warning")
        {
            return "Warning";
        }

        return "Success";
    }
}
