using System.Collections.Generic;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// The two unrequested outcomes an external-source block write can produce that neither the
/// compiler nor the re-export can see.
///
/// <para>
/// The object count is the important one. GenerateBlocksFromSource creates whatever the source
/// declares and has no notion of the object it was addressed to, so a write that landed on the
/// wrong software scope leaves the addressed block intact — it compiles clean and re-exports
/// non-empty, and postcondition verification passes. A count other than 1 is the only cheap
/// signal that this route did something besides update the single block it was pointed at, and
/// this route has no automated coverage. Same rationale as
/// <c>PlcTypeImportResult.GeneratedObjectCount</c>.
/// </para>
/// <para>
/// Siemens-free by construction so the test project can link and cover it: this is the safety
/// signal for a route nothing else can exercise offline.
/// </para>
/// </summary>
internal static class BlockSourceWriteWarnings
{
    public static List<string> Build(
        BlockAddress address,
        bool projectNodeRemoved,
        int generatedObjectCount)
    {
        var warnings = new List<string>();

        if (!projectNodeRemoved)
        {
            warnings.Add(
                "A temporary external source node could not be removed and is still in the project. "
                + "Delete it in TIA Portal under the PLC's external source files.");
        }

        if (generatedObjectCount != 1)
        {
            warnings.Add(
                $"TIA Portal generated {generatedObjectCount} objects from the submitted source; "
                + $"expected 1. Inspect '{address.ToDisplayPath()}' and its PLC for objects this "
                + "write was not addressed to.");
        }

        return warnings;
    }
}
