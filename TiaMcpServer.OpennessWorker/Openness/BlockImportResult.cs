using System;
using System.Collections.Generic;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal sealed class BlockImportResult
{
    public BlockImportResult(
        string payload,
        BlockImportOutcomeInfo outcome,
        IReadOnlyList<string>? warnings)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome));
        Warnings = warnings is null || warnings.Count == 0
            ? Array.Empty<string>()
            : new List<string>(warnings).AsReadOnly();
    }

    public string Payload { get; }

    public BlockImportOutcomeInfo Outcome { get; }

    public IReadOnlyList<string> Warnings { get; }
}
