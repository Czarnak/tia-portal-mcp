using Siemens.Engineering;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

internal static class PlcLibraryInstanceGuard
{
    internal const string LibraryInstanceEdit = "plc_library_instance_edit";
    internal const string LibraryTestInstanceDelete = "plc_library_test_instance_delete";
    internal const string StateUnverifiable = "plc_state_unverifiable";
    private const string LinkedContentMessage = "The selected PLC object is a linked library type instance. Update its library type version instead of replacing its content.";
    private const string InWorkDeleteMessage = "The selected PLC object is an in-work library test instance. Finish or discard its library type version before deleting the instance.";
    private const string ContentInspectionMessage = "The PLC object's library linkage could not be inspected.";
    private const string MissingVersionMessage = "The linked PLC object's library version is unavailable; deletion safety cannot be established.";
    private const string UnknownVersionMessage = "The linked PLC object's library version state is unknown; deletion safety cannot be established.";
    private const string DeleteInspectionMessage = "The PLC object's library-instance deletion protection could not be inspected.";
    private const string DescendantInspectionMessage = "The block group's complete descendants could not be inspected for library test instances.";

    private static readonly string[] ControlledFailureSummaries =
    {
        LibraryInstanceEdit + ": " + LinkedContentMessage,
        LibraryTestInstanceDelete + ": " + InWorkDeleteMessage,
        StateUnverifiable + ": " + ContentInspectionMessage,
        StateUnverifiable + ": " + MissingVersionMessage,
        StateUnverifiable + ": " + UnknownVersionMessage,
        StateUnverifiable + ": " + DeleteInspectionMessage,
        StateUnverifiable + ": " + DescendantInspectionMessage
    };

    internal static PlcWriteGuardInfo InspectContentUpdate(IEngineeringServiceProvider target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        try
        {
            return target.GetService<LibraryTypeInstanceInfo>() is null
                ? new PlcWriteGuardInfo()
                : Block(LibraryInstanceEdit, LinkedContentMessage);
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            return Unverifiable(ContentInspectionMessage);
        }
    }

    internal static PlcWriteGuardInfo InspectDelete(IEngineeringServiceProvider target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        try
        {
            var link = target.GetService<LibraryTypeInstanceInfo>();
            if (link is null) return new PlcWriteGuardInfo();

            var version = link.LibraryTypeVersion;
            if (version is null)
                return Unverifiable(MissingVersionMessage);

            return version.State switch
            {
                LibraryTypeVersionState.InWork => Block(LibraryTestInstanceDelete, InWorkDeleteMessage),
                LibraryTypeVersionState.Committed => new PlcWriteGuardInfo(),
                _ => Unverifiable(UnknownVersionMessage)
            };
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            return Unverifiable(DeleteInspectionMessage);
        }
    }

    internal static PlcWriteGuardInfo InspectDeleteDescendants(PlcBlockGroup group)
    {
        if (group is null) throw new ArgumentNullException(nameof(group));
        try
        {
            foreach (PlcBlock block in group.Blocks)
            {
                var result = InspectDelete(block);
                if (result.Blocked) return result;
            }

            foreach (PlcBlockGroup child in group.Groups)
            {
                var result = InspectDeleteDescendants(child);
                if (result.Blocked) return result;
            }

            return new PlcWriteGuardInfo();
        }
        catch (Exception exception) when (IsInspectionFailure(exception))
        {
            return Unverifiable(DescendantInspectionMessage);
        }
    }

    internal static void RequireAllowed(PlcWriteGuardInfo result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (result.Blocked)
            throw new WorkerOperationException(WorkerFailureCategories.GuardBlocked, $"{result.GuardId}: {result.Message}");
    }

    internal static bool TryGetControlledFailureMessage(Exception? exception, out string message)
    {
        message = string.Empty;
        if (exception is not WorkerOperationException { FailureCategory: WorkerFailureCategories.GuardBlocked } failure)
            return false;

        foreach (var summary in ControlledFailureSummaries)
        {
            if (!string.Equals(summary, failure.Message, StringComparison.Ordinal)) continue;
            message = summary;
            return true;
        }
        return false;
    }

    private static bool IsInspectionFailure(Exception exception)
        => OpennessExceptions.IsRecoverable(exception)
           && exception is not WorkerOperationException { FailureCategory: WorkerFailureCategories.PortalLost };

    private static PlcWriteGuardInfo Unverifiable(string message) => Block(StateUnverifiable, message);

    private static PlcWriteGuardInfo Block(string guardId, string message)
        => new() { Blocked = true, GuardId = guardId, Message = message };
}
