using System;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class BlockPostconditionVerifier
{
    private const string PublicFailureDetail = "verification did not complete.";
    private const string UncertainStateWarning =
        "Project state may have changed; inspect the project before retrying.";

    public static void Verify(BlockPostconditionEvidence evidence)
    {
        Verify(evidence, "update");
    }

    public static void Verify(BlockPostconditionEvidence evidence, string operation)
    {
        if (evidence is null) throw new ArgumentNullException(nameof(evidence));
        if (string.IsNullOrWhiteSpace(operation)) throw new ArgumentException("Operation is required.", nameof(operation));

        if (evidence.CompileSucceeded && evidence.ReExportSucceeded)
        {
            return;
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.PostconditionFailed,
            "Block " + operation + " postcondition failed: " + PublicFailureDetail,
            new[] { UncertainStateWarning });
    }
}
