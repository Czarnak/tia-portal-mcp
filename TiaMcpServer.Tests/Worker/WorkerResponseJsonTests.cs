using System.Text.Json;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public class WorkerResponseJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = WorkerJson.Envelope;

    [Fact]
    public void SerializesWarningsWithCamelCaseWireName()
    {
        var response = new WorkerResponse
        {
            Success = true,
            Warnings = new List<string> { "Skipping device X: access denied" }
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);

        Assert.Contains("\"warnings\":[\"Skipping device X: access denied\"]", json);
    }

    [Fact]
    public void OmitsWarningsWhenNoneWereCaptured()
    {
        var response = new WorkerResponse { Success = true };

        var json = JsonSerializer.Serialize(response, JsonOptions);

        Assert.DoesNotContain("\"warnings\"", json);
    }

    [Fact]
    public void Deserializes_ResolvedProjectPath()
    {
        const string json = """{"success":true,"payload":"{}","resolvedProjectPath":"C:\\proj\\SimpleProject.ap21"}""";

        var response = JsonSerializer.Deserialize<WorkerResponse>(json, JsonOptions);

        Assert.NotNull(response);
        Assert.Equal("C:\\proj\\SimpleProject.ap21", response!.ResolvedProjectPath);
    }

    [Fact]
    public void ResolvedProjectPath_DefaultsToNull()
    {
        const string json = """{"success":true,"payload":"{}"}""";

        var response = JsonSerializer.Deserialize<WorkerResponse>(json, JsonOptions);

        Assert.Null(response!.ResolvedProjectPath);
    }

    [Theory]
    [InlineData("validation_error")]
    [InlineData("binding_conflict")]
    [InlineData("state_changed")]
    [InlineData("worker_operation_failed")]
    [InlineData("worker_timeout")]
    [InlineData("worker_crashed")]
    [InlineData("postcondition_failed")]
    public void WorkerResponse_RoundTripsFailureCategory(string category)
    {
        var response = new WorkerResponse
        {
            Success = false,
            Error = "boom",
            FailureCategory = category
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);
        var roundTripped = JsonSerializer.Deserialize<WorkerResponse>(json, JsonOptions);

        Assert.Contains($"\"failureCategory\":\"{category}\"", json);
        Assert.NotNull(roundTripped);
        Assert.Equal(category, roundTripped!.FailureCategory);
    }

    [Theory]
    [InlineData("invalid_cursor")]
    [InlineData("cursor_filter_mismatch")]
    [InlineData("cursor_snapshot_mismatch")]
    [InlineData("cursor_out_of_range")]
    [InlineData("cursor_binding_mismatch")]
    [InlineData(WorkerFailureCategories.InvalidSelector)]
    [InlineData(WorkerFailureCategories.SnapshotTooLarge)]
    [InlineData(WorkerFailureCategories.SnapshotUnavailable)]
    [InlineData(WorkerFailureCategories.ResultItemTooLarge)]
    [InlineData(WorkerFailureCategories.ResultMetadataTooLarge)]
    public void WorkerFailureCategories_RecognizesCursorAndProjectTreeFailureCategories(string category)
    {
        Assert.True(WorkerFailureCategories.IsKnown(category));
    }

    [Theory]
    [InlineData("target_not_found")]
    [InlineData("target_ambiguous")]
    [InlineData("target_evidence_mismatch")]
    [InlineData("target_kind_unsupported")]
    public void WorkerFailureCategories_RecognizesTargetSelectionFailureCategories(string category)
    {
        Assert.True(WorkerFailureCategories.IsKnown(category));
    }

    [Fact]
    public void FailureCategory_DefaultsToNull()
    {
        const string json = """{"success":true,"payload":"{}"}""";

        var response = JsonSerializer.Deserialize<WorkerResponse>(json, JsonOptions);

        Assert.Null(response!.FailureCategory);
    }

    [Fact]
    public void OmitsFailureCategoryWhenNull()
    {
        var response = new WorkerResponse { Success = true };

        var json = JsonSerializer.Serialize(response, JsonOptions);

        Assert.DoesNotContain("\"failureCategory\"", json);
    }

    [Fact]
    public void BlockImportOutcome_RoundTripsOnFailedResponseAndOmittedOtherwise()
    {
        var outcome = new BlockImportOutcomeInfo
        {
            ImportStage = "unknown", ImportResultState = "unavailable",
            TargetMutationCommitted = null, CompileStage = "unavailable",
            FinalReadStage = "unavailable", TemporarySourceState = "unknown",
            ContentRelation = "unknown"
        };
        var response = new WorkerResponse
        {
            Success = false, Error = "bounded failure",
            FailureCategory = WorkerFailureCategories.PostconditionFailed,
            Warnings = new List<string> { "bounded warning" },
            BlockImportOutcome = outcome
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);
        var restored = JsonSerializer.Deserialize<WorkerResponse>(json, JsonOptions)!;
        Assert.Equal("unknown", restored.BlockImportOutcome!.ImportStage);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, restored.FailureCategory);
        Assert.Equal("bounded warning", Assert.Single(restored.Warnings!));
        Assert.DoesNotContain("blockImportOutcome", JsonSerializer.Serialize(
            new WorkerResponse { Success = true }, JsonOptions));
    }
}
