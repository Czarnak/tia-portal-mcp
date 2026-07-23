using System;
using System.Collections.Generic;

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
    }

    public bool CompileSucceeded { get; }

    public bool ReExportSucceeded { get; }

    public string DiagnosticMessage { get; }

    public IReadOnlyList<string> Warnings { get; }

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
