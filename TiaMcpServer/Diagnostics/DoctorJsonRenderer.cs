using System.Text;
using System.Text.Json;

namespace TiaMcpServer.Diagnostics;

public static class DoctorJsonRenderer
{
    public static string Render(DoctorReport report, bool verbose)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("status", StatusString(report.Status));
            writer.WriteString("timestampUtc", FormatTimestamp(report.TimestampUtc));
            writer.WriteString("hostVersion", report.HostVersion);

            writer.WritePropertyName("summary");
            writer.WriteStartObject();
            writer.WriteNumber("passed", report.Summary.Passed);
            writer.WriteNumber("warnings", report.Summary.Warnings);
            writer.WriteNumber("failed", report.Summary.Failed);
            writer.WriteEndObject();

            writer.WritePropertyName("checks");
            writer.WriteStartArray();
            foreach (var check in report.Checks)
            {
                writer.WriteStartObject();
                writer.WriteString("id", check.Id);
                writer.WriteString("name", check.Name);
                writer.WriteString("status", StatusString(check.Status));
                writer.WriteString("message", check.Message);
                if (!string.IsNullOrEmpty(check.Remediation))
                {
                    writer.WriteString("remediation", check.Remediation);
                }
                else
                {
                    writer.WriteNull("remediation");
                }

                if (verbose && check.Evidence is not null && check.Evidence.Count > 0)
                {
                    writer.WritePropertyName("evidence");
                    writer.WriteStartObject();
                    foreach (var kvp in check.Evidence)
                    {
                        if (kvp.Value is null)
                        {
                            writer.WriteNull(kvp.Key);
                        }
                        else
                        {
                            writer.WriteString(kvp.Key, kvp.Value);
                        }
                    }
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string StatusString(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Passed => "passed",
        DiagnosticStatus.Warning => "warning",
        DiagnosticStatus.Failed => "failed",
        _ => "unknown"
    };

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        return utc.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }
}
