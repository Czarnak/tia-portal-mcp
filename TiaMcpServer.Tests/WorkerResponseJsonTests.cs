using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests;

public class WorkerResponseJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
    public void WorkerFailureCategories_RecognizesCursorFailureCategories(string category)
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
}
