using System;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class BlockPostconditionVerifier
{
    private const string UncertainStateWarning =
        "Project state may have changed; inspect the project before retrying.";

    internal static T ReExportPrimaryDocument<T>(
        string primaryDocumentName,
        Func<string, T> exportDocuments)
    {
        if (string.IsNullOrWhiteSpace(primaryDocumentName))
        {
            throw new ArgumentException("A primary document name is required.", nameof(primaryDocumentName));
        }

        if (exportDocuments is null) throw new ArgumentNullException(nameof(exportDocuments));

        return exportDocuments(primaryDocumentName);
    }

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

    internal static bool VerifyReExportedPrimaryDocument(
        string resolvedTargetDocumentName,
        string primaryDocumentName,
        Func<string, bool> isNonEmptyDocument)
    {
        if (resolvedTargetDocumentName is null) throw new ArgumentNullException(nameof(resolvedTargetDocumentName));
        if (primaryDocumentName is null) throw new ArgumentNullException(nameof(primaryDocumentName));
        if (isNonEmptyDocument is null) throw new ArgumentNullException(nameof(isNonEmptyDocument));

        return isNonEmptyDocument(primaryDocumentName);
    }
}
