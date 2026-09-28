using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

[LegacyNullOmission(LegacyNullOmissionReason.ToolMigration)]
public class CompileCheckReport
{
    public string Scope { get; set; } = "plc";

    public string? BlockPath { get; set; }

    public List<PlcCompileInfo> Plcs { get; set; } = new List<PlcCompileInfo>();

    public int TotalErrorCount { get; set; }

    public int TotalWarningCount { get; set; }

    public string OverallState { get; set; } = "Success";
}
