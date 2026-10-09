using System.Text;
using System.Text.Json;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class JsonlWriteAuditSinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private static WriteAuditRecord Record(string tool = "network_write", DateTimeOffset? at = null)
        => new(
            WriteAuditRecord.Kind,
            WriteAuditRecord.CurrentVersion,
            at ?? Now,
            tool,
            "1",
            WriteAuditRecord.ModeName(McpAccessMode.Full),
            new WriteAuditConfirmation("policy", "not_requested"),
            @"C:\p\demo.ap21",
            new WriteAuditBinding("verified", "b1", 3, @"C:\p\demo.ap21", "w1", 2, 100, null),
            JsonDocument.Parse("""[{"operationId":"a"}]""").RootElement.Clone(),
            WritePhases.Applied,
            "{\"phase\":\"applied\"}",
            "sha256:abc",
            [new WriteAuditGuard("g1", "acknowledge", "a", "msg", true, GuardSatisfactions.Policy)],
            [new WriteAuditItem(
                "a",
                "add_network",
                "PLC_1/OB1",
                [new CheckedPrecondition("contentHash", "xml:sha256:1", "xml:sha256:1", true)],
                "succeeded",
                null,
                null,
                ["w"],
                12L)],
            34L);

    [Fact]
    public void Append_TwoRecords_WritesTwoParseableLinesWithoutBom()
    {
        using var dir = new TempAuditDirectory();
        Directory.CreateDirectory(dir.Path);
        var legacy = Path.Combine(dir.Path, "2026-09-29.jsonl");
        File.WriteAllText(legacy, "legacy\n");
        var sink = new JsonlWriteAuditSink(dir.Path);

        sink.Append(Record("one"));
        sink.Append(Record("two"));

        var path = Path.Combine(dir.Path, "writes-2026-09-29.jsonl");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal((byte)'{', bytes[0]);
        var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("write", doc.RootElement.GetProperty("recordKind").GetString());
            Assert.Equal(2, doc.RootElement.GetProperty("recordVersion").GetInt32());
        }
        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal("one", first.RootElement.GetProperty("tool").GetString());
        Assert.Equal("policy", first.RootElement.GetProperty("confirmation").GetProperty("by").GetString());
        Assert.Equal("not_requested", first.RootElement.GetProperty("confirmation").GetProperty("outcome").GetString());
        var item = first.RootElement.GetProperty("items")[0];
        Assert.Equal(12, item.GetProperty("durationMs").GetInt64());
        Assert.Equal("policy", first.RootElement.GetProperty("guards")[0].GetProperty("satisfiedBy").GetString());
        Assert.Equal("b1", first.RootElement.GetProperty("binding").GetProperty("bindingId").GetString());
        Assert.Equal(34, first.RootElement.GetProperty("durationMs").GetInt64());
        Assert.Equal("w", item.GetProperty("warnings")[0].GetString());
        Assert.Equal("legacy\n", File.ReadAllText(legacy));
    }

    [Fact]
    public void Append_NullDuration_IsWrittenAsExplicitNull()
    {
        using var dir = new TempAuditDirectory();
        var sink = new JsonlWriteAuditSink(dir.Path);
        var record = Record() with { Items = [new WriteAuditItem("a", "op", null, [], "skipped", "target_not_found", "gone", [], null)] };

        sink.Append(record);

        var line = File.ReadAllText(Path.Combine(dir.Path, "writes-2026-09-29.jsonl"));
        using var doc = JsonDocument.Parse(line);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("items")[0].GetProperty("durationMs").ValueKind);
    }

    [Fact]
    public void FileNameFor_UsesUtcDateAcrossMidnight()
    {
        var local = new DateTimeOffset(2026, 9, 30, 0, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal("writes-2026-09-29.jsonl", JsonlWriteAuditSink.FileNameFor(local));
    }

    [Fact]
    public void Append_DirectoryPathIsAFile_IsSwallowed()
    {
        var file = Path.GetTempFileName();
        try
        {
            var sink = new JsonlWriteAuditSink(file);
            var original = Console.Error;
            var captured = new StringWriter();
            Console.SetError(captured);

            Exception? ex;
            try { ex = Xunit.Record.Exception(() => sink.Append(Record())); }
            finally { Console.SetError(original); }

            Assert.Null(ex);
            Assert.Contains("failed to write audit record for 'network_write'", captured.ToString());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ModeName_SpellsBothModes()
    {
        Assert.Equal("read-only", WriteAuditRecord.ModeName(McpAccessMode.ReadOnly));
        Assert.Equal("read-write", WriteAuditRecord.ModeName(McpAccessMode.ReadWrite));
    }
}
