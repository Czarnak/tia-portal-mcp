using TiaMcpServer.Contracts;

namespace TiaMcpServer.Diagnostics;

public static class DoctorTextRenderer
{
    public static void Render(DoctorReport report, bool verbose, TextWriter writer, McpAccessMode? accessMode = null)
    {
        writer.WriteLine("TIA Portal MCP Environment Doctor");
        writer.WriteLine();
        if (accessMode.HasValue)
        {
            var modeLabel = accessMode.Value == McpAccessMode.ReadOnly ? "READ-ONLY" : "READ-WRITE";
            writer.WriteLine($"Access mode: {modeLabel}");
            writer.WriteLine();
        }

        foreach (var check in report.Checks)
        {
            RenderCheck(writer, check, verbose);
        }

        writer.WriteLine($"Summary: {report.Summary.Passed} passed, {Pluralize(report.Summary.Warnings, "warning")}, {Pluralize(report.Summary.Failed, "failure")}");
        writer.WriteLine(report.Status == DiagnosticStatus.Failed
            ? "Environment is not ready."
            : report.Status == DiagnosticStatus.Warning
                ? "Environment is ready with warnings."
                : "Environment is ready.");
    }

    private static void RenderCheck(TextWriter writer, DiagnosticCheckResult check, bool verbose)
    {
        var tag = check.Status switch
        {
            DiagnosticStatus.Passed => "[PASS]",
            DiagnosticStatus.Warning => "[WARN]",
            DiagnosticStatus.Failed => "[FAIL]",
            _ => "[????]"
        };

        writer.WriteLine($"{tag} {check.Name}");
        writer.WriteLine($"       {check.Message}");

        if (!string.IsNullOrEmpty(check.Remediation))
        {
            writer.WriteLine();
            writer.WriteLine("       Fix:");
            foreach (var line in SplitLines(check.Remediation))
            {
                writer.WriteLine($"       {line}");
            }
        }

        if (verbose && check.Evidence is not null && check.Evidence.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("       Evidence:");
            foreach (var kvp in check.Evidence)
            {
                writer.WriteLine($"         {kvp.Key} = {FormatValue(kvp.Value)}");
            }
        }

        writer.WriteLine();
    }

    private static string FormatValue(string? value) => value is null ? "<null>" : value;

    private static IEnumerable<string> SplitLines(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            yield return line;
        }
    }

    private static string Pluralize(int count, string singular) => count == 1 ? $"{count} {singular}" : $"{count} {singular}s";
}
