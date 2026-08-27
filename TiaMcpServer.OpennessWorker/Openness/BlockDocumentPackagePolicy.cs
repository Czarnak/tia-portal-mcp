using System;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>The outcome of asking what a failed s7dcl document-package export means.</summary>
internal sealed class BlockDocumentPackageOutcome
{
    private BlockDocumentPackageOutcome(bool isFatal, string message)
    {
        IsFatal = isFatal;
        Message = message;
    }

    /// <summary>True when no document survived and the bundle would be empty.</summary>
    public bool IsFatal { get; }

    /// <summary>Error text when <see cref="IsFatal"/>; warning text otherwise. Never empty.</summary>
    public string Message { get; }

    public static BlockDocumentPackageOutcome Fatal(string message)
        => new BlockDocumentPackageOutcome(true, message);

    public static BlockDocumentPackageOutcome Degraded(string message)
        => new BlockDocumentPackageOutcome(false, message);
}

/// <summary>
/// Decides whether a failed s7dcl document-package export should fail the whole read.
///
/// <para>
/// The bundle carries two kinds of document. The Simatic ML XML is authoritative: it is what
/// update_block_logic consumes. The s7dcl package is human-readable rung text and is read-only
/// context. Treating a missing supplementary document as fatal loses the authoritative one too,
/// which is the defect this type exists to end — S7-300/S7-400 CPUs never support document
/// export, so every block on those CPUs was unreadable even though its XML exported cleanly.
/// </para>
/// <para>
/// The rule is therefore the bundle's own invariant rather than a CPU-family list: a bundle is
/// unusable only when it would contain no documents at all. Keying off the surviving document
/// avoids matching Siemens message text or maintaining a hard-coded device-family table, both of
/// which would rot.
/// </para>
/// <para>
/// Siemens-free by construction so the test project can link and cover it: the caller reduces the
/// live export result to a bool and a detail string.
/// </para>
/// </summary>
internal static class BlockDocumentPackagePolicy
{
    /// <summary>Keeps a Siemens exception message from dominating the warning list.</summary>
    internal const int MaxDetailLength = 300;

    public static BlockDocumentPackageOutcome Decide(
        bool hasPrimaryXmlDocument,
        string displayPath,
        string failureDetail)
    {
        var detail = Summarize(failureDetail);

        if (!hasPrimaryXmlDocument)
        {
            return BlockDocumentPackageOutcome.Fatal(
                $"No document could be exported for '{displayPath}'. The Simatic ML XML export did "
                + $"not succeed and the s7dcl document package failed: {detail}");
        }

        return BlockDocumentPackageOutcome.Degraded(
            $"Human-readable rung text (s7dcl) is unavailable for '{displayPath}': {detail} The "
            + "Simatic ML XML document was exported, so this payload is complete for reading and "
            + "for update_block_logic. CPUs of the S7-300/S7-400 series never support this "
            + "document package.");
    }

    /// <summary>
    /// Reduces a Siemens message to one bounded single-line clause. Newlines are collapsed because
    /// the worker splits captured stderr on newlines, so a multi-line warning would arrive as
    /// several unrelated-looking warnings.
    /// </summary>
    private static string Summarize(string failureDetail)
    {
        if (string.IsNullOrWhiteSpace(failureDetail))
        {
            return "TIA Portal reported no detail.";
        }

        var collapsed = failureDetail
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        while (collapsed.Contains("  "))
        {
            collapsed = collapsed.Replace("  ", " ");
        }

        if (collapsed.Length > MaxDetailLength)
        {
            collapsed = collapsed.Substring(0, MaxDetailLength - 1) + "…";
        }

        return collapsed.EndsWith(".", StringComparison.Ordinal) ? collapsed : collapsed + ".";
    }
}
