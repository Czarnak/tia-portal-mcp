using System;
using System.Collections.Generic;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// The one thing a source read can produce that the caller cannot see from the payload alone: a
/// document that will be refused if they try to write it back.
///
/// <para>
/// withDependencies=true asks Openness for a block's dependency closure, which is genuinely useful
/// context — but a write accepts exactly one declared object, so that document is a dead end for
/// editing. Saying so in a warning is cheaper than letting the caller discover it by having a
/// write rejected, and it keeps the payload itself clean SCL rather than SCL with an injected
/// banner comment.
/// </para>
/// <para>
/// Siemens-free by construction so the test project can link and cover it.
/// </para>
/// </summary>
internal static class SourceReadWarnings
{
    public static IReadOnlyList<string> ForExport(bool withDependencies, string format, string content)
    {
        if (!withDependencies || !string.Equals(format, SourceFormatNames.Source, StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        var declarations = SourceDeclarationScanner.Scan(content);

        if (declarations.Count <= 1)
        {
            return Array.Empty<string>();
        }

        return new[]
        {
            $"This document was read with withDependencies=true and declares {declarations.Count} "
            + $"objects: {SourceDeclarationScanner.Describe(declarations)}. It is context only — a "
            + "write refuses any source declaring more than one object. Re-read with "
            + "withDependencies omitted to get a document you can edit and submit back."
        };
    }
}
