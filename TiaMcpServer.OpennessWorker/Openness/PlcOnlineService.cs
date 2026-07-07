using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.Online;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class PlcOnlineService
{
    public static PlcOnlineResultInfo Start(Project project, string? plcName)
        => ControlPlc(project, plcName, "Start", "start_plc");

    public static PlcOnlineResultInfo Stop(Project project, string? plcName)
        => ControlPlc(project, plcName, "Stop", "stop_plc");

    private static PlcOnlineResultInfo ControlPlc(
        Project project,
        string? plcName,
        string methodName,
        string operation)
    {
        string? resolvedPlcName = null;

        foreach (var plc in PlcSoftwareLocator.FindAll(project, plcName))
        {
            resolvedPlcName = plc.DeviceName;

            var onlineProvider = plc.Software.GetService<OnlineProvider>()
                ?? throw new InvalidOperationException(
                    $"OnlineProvider service is not available on PLC '{plc.DeviceName}'. " +
                    "Ensure the PLC is reachable and the project is compiled.");

            InvokeControlMethod(onlineProvider, methodName, plc.DeviceName);
            break;
        }

        if (resolvedPlcName is null)
        {
            var detail = plcName is null ? string.Empty : $" named '{plcName}'";
            throw new InvalidOperationException($"No PLC software{detail} was found in the project.");
        }

        return new PlcOnlineResultInfo
        {
            Operation = operation,
            ProjectPath = project.Path.FullName,
            PlcName = resolvedPlcName
        };
    }

    // Start() and Stop() are not declared on the compile-time Openness stub;
    // resolved at runtime from the full V21 assembly (same pattern as CompileChecker.ReadMessagePath).
    private static void InvokeControlMethod(OnlineProvider onlineProvider, string methodName, string plcName)
    {
        var method = onlineProvider.GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

        if (method is null)
        {
            throw new InvalidOperationException(
                $"OnlineProvider.{methodName}() is not available in this TIA Portal version. " +
                $"PLC: '{plcName}'. Verify the PLC is online before calling this operation.");
        }

        try
        {
            method.Invoke(onlineProvider, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
