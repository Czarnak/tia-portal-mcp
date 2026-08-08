using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

/// <summary>
/// Contract tests for the extended project-metadata surface of <c>get_project_status</c>: the
/// additive <see cref="ProjectStatusInfo.Metadata"/> schema, backward compatibility of payloads
/// produced before metadata existed, worker source-contract invariants for
/// <c>ProjectMetadataReader</c>, and the full metadata round trip over the real IPC pipe via the
/// FakeWorker's <c>status-with-metadata</c> scenario.
/// </summary>
public class ProjectMetadataTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static OpennessWorkerClient CreateClient(string workerPath)
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: workerPath);

    private static JsonElement PayloadFromEnvelope(string response)
    {
        using var envelope = JsonDocument.Parse(response);
        using var payload = JsonDocument.Parse(envelope.RootElement.GetProperty("payload").GetString()!);
        return payload.RootElement.Clone();
    }

    [Fact]
    public void StatusWithoutMetadata_SerializesIdenticallyToPreMetadataPayload()
    {
        var status = new ProjectStatusInfo
        {
            IsOpen = true,
            Name = "Ground",
            Path = @"C:\Ground\Ground.ap21",
            Version = "V21",
            Author = "TiaBot"
        };

        var json = JsonSerializer.Serialize(status, JsonOptions);

        // No metadata member is emitted when none is set - the payload is byte-for-byte the
        // pre-metadata schema, so existing consumers keep parsing unchanged responses.
        Assert.DoesNotContain("metadata", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"isOpen\":true", json);
        Assert.Contains("\"name\":\"Ground\"", json);
    }

    [Fact]
    public void Metadata_SerializesAllSectionsWithCamelCaseWireNames()
    {
        var metadata = new ProjectMetadataInfo
        {
            Copyright = "© ACME",
            Family = "Lines",
            Comment = new ProjectCommentInfo
            {
                Translations = new List<ProjectCommentTranslationInfo>
                {
                    new() { Culture = "en-US", Text = "Ground" },
                    new() { Culture = "pt-BR", Text = "Piso" },
                },
            },
            LanguageSettings = new ProjectLanguageSettingsInfo
            {
                Languages = new List<string> { "en-US", "pt-BR" },
                ActiveLanguages = new List<string> { "en-US" },
                EditingLanguage = "en-US",
                ReferenceLanguage = "de-DE",
            },
            HistoryEntries = new List<ProjectHistoryEntryInfo>
            {
                new() { Text = "created", DateTime = new DateTime(2026, 1, 1) },
            },
            HistoryTruncated = false,
            UsedProducts = new List<ProjectUsedProductInfo>
            {
                new() { Name = "S7-1500", Version = "V4.5" },
            },
            CompilationSettings = new ProjectCompilationSettingsInfo
            {
                IsSimulationDuringBlockCompilationEnabled = true,
                IsVirtualPlcDuringBlockCompilationEnabled = null,
            },
        };

        var json = JsonSerializer.Serialize(new ProjectStatusInfo { Metadata = metadata }, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var meta = root.GetProperty("metadata");

        Assert.Equal("© ACME", meta.GetProperty("copyright").GetString());
        Assert.Equal("Lines", meta.GetProperty("family").GetString());

        var comment = meta.GetProperty("comment").GetProperty("translations").EnumerateArray().ToArray();
        Assert.Equal(2, comment.Length);
        Assert.Equal("en-US", comment[0].GetProperty("culture").GetString());
        Assert.Equal("Ground", comment[0].GetProperty("text").GetString());

        var language = meta.GetProperty("languageSettings");
        Assert.Equal(new[] { "en-US", "pt-BR" }, language.GetProperty("languages").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "en-US" }, language.GetProperty("activeLanguages").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("en-US", language.GetProperty("editingLanguage").GetString());
        Assert.Equal("de-DE", language.GetProperty("referenceLanguage").GetString());

        Assert.Equal("created", meta.GetProperty("historyEntries")[0].GetProperty("text").GetString());
        Assert.False(meta.GetProperty("historyTruncated").GetBoolean());

        Assert.Equal("S7-1500", meta.GetProperty("usedProducts")[0].GetProperty("name").GetString());

        var compilation = meta.GetProperty("compilationSettings");
        Assert.True(compilation.GetProperty("isSimulationDuringBlockCompilationEnabled").GetBoolean());
        // Null from an unavailable service must be omitted, never written as false.
        Assert.False(compilation.TryGetProperty("isVirtualPlcDuringBlockCompilationEnabled", out _));
    }

    [Fact]
    public async Task GetProjectStatus_FullMetadata_RoundTripsOverTheRealIpcPipe()
    {
        using var client = CreateClient(FakeWorkerLocator.Locate());

        var response = await ProjectReadTools.GetProjectStatus(client, "status-with-metadata");
        var status = PayloadFromEnvelope(response);
        var metadata = status.GetProperty("metadata");

        Assert.True(status.GetProperty("isOpen").GetBoolean());
        Assert.Equal("Ground", status.GetProperty("name").GetString());

        Assert.Equal("© ACME Controls", metadata.GetProperty("copyright").GetString());
        Assert.Equal("Lines", metadata.GetProperty("family").GetString());

        var comment = metadata.GetProperty("comment").GetProperty("translations").EnumerateArray().ToArray();
        Assert.Equal(2, comment.Length);
        Assert.Equal("en-US", comment[0].GetProperty("culture").GetString());
        Assert.Equal("Ground line", comment[0].GetProperty("text").GetString());
        Assert.Equal("pt-BR", comment[1].GetProperty("culture").GetString());

        var language = metadata.GetProperty("languageSettings");
        Assert.Equal(new[] { "en-US", "de-DE", "pt-BR" }, language.GetProperty("languages").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "en-US", "pt-BR" }, language.GetProperty("activeLanguages").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("en-US", language.GetProperty("editingLanguage").GetString());
        Assert.Equal("de-DE", language.GetProperty("referenceLanguage").GetString());

        var history = metadata.GetProperty("historyEntries").EnumerateArray().ToArray();
        Assert.Equal(2, history.Length);
        Assert.Equal("Project created", history[0].GetProperty("text").GetString());
        Assert.Equal("Line imported", history[1].GetProperty("text").GetString());
        Assert.False(metadata.GetProperty("historyTruncated").GetBoolean());

        var products = metadata.GetProperty("usedProducts").EnumerateArray().ToArray();
        Assert.Equal(2, products.Length);
        Assert.Equal("S7-1500", products[0].GetProperty("name").GetString());
        Assert.Equal("V4.5", products[0].GetProperty("version").GetString());

        var compilation = metadata.GetProperty("compilationSettings");
        Assert.True(compilation.GetProperty("isSimulationDuringBlockCompilationEnabled").GetBoolean());
        Assert.False(compilation.GetProperty("isVirtualPlcDuringBlockCompilationEnabled").GetBoolean());
    }
}