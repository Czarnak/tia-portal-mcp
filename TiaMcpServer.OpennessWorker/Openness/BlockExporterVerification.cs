using System;
using System.Collections.Generic;
using System.IO;

namespace TiaMcpServer.OpennessWorker.Openness;

public static partial class BlockExporter
{
    internal static BlockPostconditionEvidence VerifyPrimaryDocument(
        string resolvedTargetDocumentName,
        string primaryDocumentName,
        Func<DirectoryInfo, string, bool> exportDocuments,
        Action<string>? cleanupDirectory = null)
    {
        string? verificationDirectory = null;
        var warnings = new List<string>();
        var reExportSucceeded = false;
        var diagnosticMessage = "Re-exported primary document was not verified.";

        try
        {
            if (string.IsNullOrWhiteSpace(resolvedTargetDocumentName))
            {
                throw new ArgumentException("A resolved target document name is required.", nameof(resolvedTargetDocumentName));
            }

            if (string.IsNullOrWhiteSpace(primaryDocumentName))
            {
                throw new ArgumentException("A primary document name is required.", nameof(primaryDocumentName));
            }

            if (exportDocuments is null) throw new ArgumentNullException(nameof(exportDocuments));

            verificationDirectory = Path.Combine(Path.GetTempPath(), "tia-mcp-verify-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(verificationDirectory);
            reExportSucceeded = exportDocuments(new DirectoryInfo(verificationDirectory), resolvedTargetDocumentName);
            diagnosticMessage = reExportSucceeded
                ? "Verified."
                : "Re-export did not produce a non-empty primary document.";
        }
        catch (Exception exception)
        {
            diagnosticMessage = "Re-export could not complete after block import: " + exception.Message;
        }
        finally
        {
            if (!string.IsNullOrEmpty(verificationDirectory) && Directory.Exists(verificationDirectory))
            {
                try
                {
                    (cleanupDirectory ?? DeleteVerificationDirectory)(verificationDirectory!);
                }
                catch (Exception exception)
                {
                    var warning = "Block update verification cleanup failed: " + exception.Message;
                    warnings.Add(warning.Length <= 512 ? warning : warning.Substring(0, 512));
                }
            }
        }

        return new BlockPostconditionEvidence(
            compileSucceeded: true,
            reExportSucceeded: reExportSucceeded,
            diagnosticMessage: diagnosticMessage,
            warnings: warnings);
    }

    private static void DeleteVerificationDirectory(string path)
    {
        Directory.Delete(path, recursive: true);
    }
}
