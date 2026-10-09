using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.TestUtilities;

internal static class LifecycleTestCalls
{
    internal static WriteExecution Execution(OpennessWorkerClient client, TempAuditDirectory audit)
        => new(new OpennessWriteBindingGate(client), new JsonlWriteAuditSink(audit.Path),
            LifecycleWriteDomain.Catalog, TimeProvider.System);

    internal static JsonElement Document(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var document = JsonDocument.Parse(text);
        Assert.Equal(document.RootElement.GetRawText(), result.StructuredContent!.Value.GetRawText());
        return document.RootElement.Clone();
    }

    internal static void Rejected(CallToolResult result, string category)
    {
        var document = Document(result);
        Assert.True(result.IsError, document.GetRawText());
        Assert.False(document.GetProperty("success").GetBoolean());
        Assert.Equal(category, document.GetProperty("error").GetProperty("category").GetString());
    }

    internal static void Phase(JsonElement document, string expected)
        => Assert.True(document.GetProperty("phase").GetString() == expected, document.GetRawText());

    internal static void Succeeded(JsonElement document)
        => Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());

    internal static int AuditCount(TempAuditDirectory audit)
        => Directory.Exists(audit.Path)
            ? Directory.GetFiles(audit.Path, "*.jsonl").Sum(file => File.ReadAllLines(file).Length)
            : 0;
}
