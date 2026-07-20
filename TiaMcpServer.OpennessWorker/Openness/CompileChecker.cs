using System;
using System.Collections.Generic;
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
        if (!string.IsNullOrWhiteSpace(blockPath))
        {
            return CompileBlock(project, plcName, blockPath!);
        }

        return CompilePlcSoftware(project, plcName);
    }

    private static CompileCheckReport CompileBlock(Project project, string? plcName, string blockPath)
    {
        var address = BlockAddress.Parse(blockPath);
        if (address.PlcName == null && !string.IsNullOrWhiteSpace(plcName))
        {
            address = BlockAddress.Parse(plcName + "/" + blockPath);
        }

        var target = BlockTargetResolver.ResolveForExport(project, address);

        if (target.Block == null)
        {
            throw new InvalidOperationException($"Block '{address.BlockName}' not found.");
        }

        var result = CompileObject(target.Block);
        string resolvedPlcName = address.PlcName ?? string.Empty;
        var usedFirstPlc = false;
        if (string.IsNullOrEmpty(resolvedPlcName))
        {
            resolvedPlcName = FindFirstDeviceName(project) ?? string.Empty;
            usedFirstPlc = true;
        }

        var plc = BuildPlcCompileInfo(resolvedPlcName, result);
        if (usedFirstPlc)
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

    private static string? FindFirstDeviceName(Project project)
    {
        return PlcSoftwareLocator.FindAll(project, null).FirstOrDefault()?.DeviceName;
    }

    private static CompileCheckReport CompilePlcSoftware(Project project, string? plcName)
    {
        var report = new CompileCheckReport
        {
            Scope = "plc",
            OverallState = "Success"
        };

        foreach (var plc in PlcSoftwareLocator.FindAll(project, plcName))
        {
            try
            {
                var result = CompileObject(plc.Software);
                report.Plcs.Add(BuildPlcCompileInfo(plc.DeviceName, result));
            }
            catch (EngineeringException ex)
            {
                var failed = new PlcCompileInfo
                {
                    PlcName = plc.DeviceName,
                    State = "Error"
                };
                failed.DiagnosticNotes.Add($"Compile failed for PLC '{plc.DeviceName}': {ex.Message}");
                report.Plcs.Add(failed);
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

    private static PlcCompileInfo BuildPlcCompileInfo(string plcName, CompilerResult result)
    {
        return new PlcCompileInfo
        {
            PlcName = plcName,
            State = MapState(result.State),
            ErrorCount = result.ErrorCount,
            WarningCount = result.WarningCount,
            Messages = MapMessages(result.Messages)
        };
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

    private static List<CompileMessageInfo> MapMessages(IEnumerable<CompilerResultMessage> messages)
    {
        var result = new List<CompileMessageInfo>();

        foreach (CompilerResultMessage message in messages)
        {
            result.Add(new CompileMessageInfo
            {
                Description = message.Description,
                Path = ReadMessagePath(message),
                Severity = MapMessageSeverity(message)
            });
        }

        return result;
    }

    private static string MapMessageSeverity(CompilerResultMessage message)
    {
        if (message.ErrorCount > 0)
        {
            return "Error";
        }

        if (message.WarningCount > 0)
        {
            return "Warning";
        }

        return "Information";
    }

    private static string ReadMessagePath(CompilerResultMessage message)
    {
        // Path is not declared on the compile-time Openness stub; resolved at runtime from the full V21 assembly.
        PropertyInfo? property = message.GetType().GetProperty("Path");
        return property?.GetValue(message, null)?.ToString() ?? string.Empty;
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
