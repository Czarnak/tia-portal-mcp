using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Contracts.Vci;

public sealed class VciBreadcrumb
{
    public VciBreadcrumb(string requestId, string operationId, string operation, List<ProjectTreeSelectorSegment> leafPath, string? format)
    {
        RequestId = requestId;
        OperationId = operationId;
        Operation = operation;
        LeafPath = leafPath;
        Format = format;
    }

    [JsonRequired] public string RequestId { get; set; }
    [JsonRequired] public string OperationId { get; set; }
    [JsonRequired] public string Operation { get; set; }
    [JsonRequired] public List<ProjectTreeSelectorSegment> LeafPath { get; set; }
    [JsonRequired] public string? Format { get; set; }
}

public sealed class VciLastAttemptedInfo
{
    public List<ProjectTreeSelectorSegment> Path { get; set; } = new();
    public string Operation { get; set; } = string.Empty;
    public string? Format { get; set; }
}

public static class VciBreadcrumbFile
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TiaMcpServer", "vci-breadcrumb.json");

    public static bool TryWrite(string path, VciBreadcrumb breadcrumb)
    {
        try
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(path, JsonSerializer.Serialize(breadcrumb, Options));
            return true;
        }
        catch { return false; }
    }

    public static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }

    public static VciLastAttemptedInfo? Take(string path, string requestId)
    {
        try
        {
            var breadcrumb = JsonSerializer.Deserialize<VciBreadcrumb>(File.ReadAllText(path), Options);
            if (breadcrumb is null || !string.Equals(breadcrumb.RequestId, requestId, StringComparison.Ordinal)
                || string.IsNullOrEmpty(breadcrumb.OperationId) || string.IsNullOrEmpty(breadcrumb.Operation)
                || breadcrumb.LeafPath is null || breadcrumb.LeafPath.Any(segment => segment is null
                    || string.IsNullOrEmpty(segment.NodeType) || string.IsNullOrEmpty(segment.Name))) return null;
            return new VciLastAttemptedInfo { Path = breadcrumb.LeafPath, Operation = breadcrumb.Operation, Format = breadcrumb.Format };
        }
        catch { return null; }
        finally { TryDelete(path); }
    }
}
