namespace TiaMcpServer.Contracts;

/// <summary>
/// Bounded evidence for one update_block_logic attempt. Commitment refers only to the in-memory
/// target call returning normally; it does not imply save, download, or requested-content equality.
/// </summary>
public sealed record BlockImportOutcomeInfo
{
    public string ImportStage { get; init; } = "not_started";
    public string ImportResultState { get; init; } = "unavailable";
    public bool? TargetMutationCommitted { get; init; }
    public string CompileStage { get; init; } = "not_started";
    public CompileCheckReport? CompileReport { get; init; }
    public bool CompileDetailsOmitted { get; init; }
    public string FinalReadStage { get; init; } = "not_started";
    public bool? TargetPresent { get; init; }
    public string ContentRelation { get; init; } = "unknown";
    public string TemporarySourceState { get; init; } = "unknown";
}
