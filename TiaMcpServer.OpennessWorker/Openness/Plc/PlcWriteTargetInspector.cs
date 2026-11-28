using Siemens.Engineering;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

internal static class PlcWriteTargetInspector
{
    internal static PlcWriteGuardInfo Inspect(ProjectBase project, PlcWriteGuardRequestInfo? request)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (request is null || !PlcWriteGuardOperation.IsSupported(request.Operation))
            throw InvalidRequest();

        var isTypeUpdate = request.Operation == PlcWriteGuardOperation.UpdateTypeContent;
        if (isTypeUpdate
            ? string.IsNullOrWhiteSpace(request.TypePath) || request.BlockPath is not null
            : string.IsNullOrWhiteSpace(request.BlockPath) || request.TypePath is not null)
        {
            throw InvalidRequest();
        }

        return request.Operation switch
        {
            PlcWriteGuardOperation.UpdateBlockLogic => PlcLibraryInstanceGuard.InspectContentUpdate(
                BlockTargetResolver.ResolveForExport(project, BlockAddress.Parse(request.BlockPath!)).Block!),
            PlcWriteGuardOperation.UpdateTypeContent => PlcLibraryInstanceGuard.InspectContentUpdate(
                PlcTypeTargetResolver.ResolveForExport(project, PlcTypeAddress.Parse(request.TypePath!)).Type!),
            PlcWriteGuardOperation.DeleteBlock => PlcLibraryInstanceGuard.InspectDelete(
                BlockTargetResolver.ResolveForExport(project, BlockAddress.Parse(request.BlockPath!)).Block!),
            PlcWriteGuardOperation.DeleteBlockGroup => PlcLibraryInstanceGuard.InspectDeleteDescendants(
                BlockTargetResolver.ResolveDeleteBlockGroup(project, request.BlockPath!).Group),
            _ => throw InvalidRequest()
        };
    }

    private static WorkerOperationException InvalidRequest()
        => new(WorkerFailureCategories.ValidationError,
            "A supported PLC inspection operation and exactly its blockPath or typePath are required.");
}
