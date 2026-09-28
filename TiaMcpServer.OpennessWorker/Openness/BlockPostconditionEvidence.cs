namespace TiaMcpServer.OpennessWorker.Openness;

internal sealed class BlockPostconditionEvidence
{
    public BlockPostconditionEvidence(
        bool compileSucceeded,
        bool reExportSucceeded,
        string diagnosticMessage,
        IReadOnlyList<string>? warnings = null)
    {
        CompileSucceeded = compileSucceeded;
        ReExportSucceeded = reExportSucceeded;
        DiagnosticMessage = Sanitize(diagnosticMessage);
        Warnings = CopyWarnings(warnings);
        CompileObservation = BlockCompileObservation.NotStarted();
        FinalReadStage = reExportSucceeded ? "succeeded" : "unavailable";
        TargetPresent = reExportSucceeded ? true : null;
    }

    private BlockPostconditionEvidence(
        BlockCompileObservation compileObservation,
        string finalReadStage,
        bool? targetPresent,
        IReadOnlyList<string>? warnings)
    {
        CompileObservation = compileObservation
            ?? throw new ArgumentNullException(nameof(compileObservation));
        if (finalReadStage != "not_started"
            && finalReadStage != "succeeded"
            && finalReadStage != "unavailable")
        {
            throw new ArgumentException(
                "Final read stage must be not_started, succeeded, or unavailable.",
                nameof(finalReadStage));
        }
        if (finalReadStage == "not_started" && targetPresent is not null)
            throw new ArgumentException("A non-started final read cannot report presence evidence.", nameof(targetPresent));
        if (finalReadStage == "succeeded" && targetPresent is null)
            throw new ArgumentException("A successful final read requires presence evidence.", nameof(targetPresent));

        FinalReadStage = finalReadStage;
        TargetPresent = targetPresent;
        CompileSucceeded = compileObservation.Stage == "succeeded";
        ReExportSucceeded = finalReadStage == "succeeded" && targetPresent == true;
        DiagnosticMessage = "Postcondition evidence is structured.";
        Warnings = CopyWarnings(warnings);
    }

    public static BlockPostconditionEvidence Import(
        BlockCompileObservation compileObservation,
        string finalReadStage,
        bool? targetPresent,
        IReadOnlyList<string>? warnings = null) =>
        new BlockPostconditionEvidence(
            compileObservation,
            finalReadStage,
            targetPresent,
            warnings);

    public bool CompileSucceeded { get; }

    public bool ReExportSucceeded { get; }

    public string DiagnosticMessage { get; }

    public IReadOnlyList<string> Warnings { get; }

    public BlockCompileObservation CompileObservation { get; }

    public string FinalReadStage { get; }

    public bool? TargetPresent { get; }

    private static IReadOnlyList<string> CopyWarnings(IReadOnlyList<string>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return Array.Empty<string>();
        }

        var copy = new List<string>(warnings.Count);
        foreach (var warning in warnings)
        {
            copy.Add(Sanitize(warning));
        }

        return copy.AsReadOnly();
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Postcondition verification did not provide diagnostic evidence.";
        }

        var source = value ?? string.Empty;
        var characters = new List<char>(source.Length);
        foreach (var character in source)
        {
            characters.Add(char.IsControl(character) ? ' ' : character);
        }

        var sanitized = new string(characters.ToArray()).Trim();
        return sanitized.Length <= 512 ? sanitized : sanitized.Substring(0, 512);
    }
}
