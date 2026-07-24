using System;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class BlockPostconditionVerifier
{
    private const string UncertainStateWarning =
        "Project state may have changed; inspect the project before retrying.";

    public static void Verify(BlockPostconditionEvidence evidence)
    {
        if (evidence is null) throw new ArgumentNullException(nameof(evidence));

        if (evidence.CompileSucceeded && evidence.ReExportSucceeded)
        {
            return;
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.PostconditionFailed,
            "Block update postcondition failed: " + evidence.DiagnosticMessage,
            new[] { UncertainStateWarning });
    }
}
