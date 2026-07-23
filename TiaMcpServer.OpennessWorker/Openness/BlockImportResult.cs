using System;
using System.Collections.Generic;

namespace TiaMcpServer.OpennessWorker.Openness;

internal sealed class BlockImportResult
{
    public BlockImportResult(string payload, IReadOnlyList<string>? warnings)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Warnings = warnings is null || warnings.Count == 0
            ? Array.Empty<string>()
            : new List<string>(warnings).AsReadOnly();
    }

    public string Payload { get; }

    public IReadOnlyList<string> Warnings { get; }
}
