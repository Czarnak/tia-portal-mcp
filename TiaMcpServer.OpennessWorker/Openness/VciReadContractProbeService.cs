using System;
using Siemens.Engineering;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Resolves and executes exactly one <c>probe_vci_read_contract</c> case against a live TIA
/// Portal project, returning the normalized <see cref="VciProbeCaseResultInfo"/> evidence the
/// wire contract describes.
///
/// <para>
/// Deliberately compile-only for VCI Workspace Phase 1 Task 2: <see cref="Execute"/> throws
/// <see cref="NotImplementedException"/> for every call. It exists so
/// <c>Program.ProbeVciReadContract</c> can be wired to a real dispatch target — one that
/// authorizes, deserializes, and semantically validates a request exactly like every other worker
/// operation — without yet touching Siemens Openness VCI reads. Task 6+ replaces this body with
/// the actual case dispatch (service/group/workspace/mapping/format resolution and outcome
/// normalization); until then, any live invocation surfaces as a
/// <see cref="WorkerFailureCategories.WorkerOperationFailed"/> failure via <c>Program</c>'s
/// general exception handler, never a silent or partially-correct result.
/// </para>
/// </summary>
internal static class VciReadContractProbeService
{
    public static VciProbeCaseResultInfo Execute(Project project, VciProbeRequestInfo request)
    {
        throw new NotImplementedException(
            "VciReadContractProbeService.Execute is a compile-only shell (VCI Workspace Phase 1 "
            + "Task 2). Case dispatch and Siemens VCI reads are implemented in a later task.");
    }
}
