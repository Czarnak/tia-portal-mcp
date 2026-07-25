using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class WorkerCallResultTests
{
    [Fact]
    public void Ok_CarriesPayloadWithoutError()
    {
        var result = WorkerCallResult.Ok("{\"a\":1}");

        Assert.True(result.Success);
        Assert.Equal("{\"a\":1}", result.Payload);
        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Ok_HasNoFailureCategory()
    {
        Assert.Null(WorkerCallResult.Ok("data").FailureCategory);
    }

    [Fact]
    public void Fail_CarriesErrorAndEmptyPayload()
    {
        var result = WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "boom");

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Payload);
        Assert.Equal("boom", result.Error);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.FailureCategory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not_an_approved_category")]
    public void Fail_RequiresOneApprovedCategory(string? category)
    {
        Assert.Throws<ArgumentException>(() => WorkerCallResult.Fail(category!, "boom"));
    }

    [Fact]
    public void ToText_RendersPayloadOnSuccess()
    {
        Assert.Equal("data", WorkerCallResult.Ok("data").ToText());
    }

    [Fact]
    public void ToText_RendersErrorPrefixOnFailure()
    {
        Assert.Equal(
            "Error: boom",
            WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "boom").ToText());
    }

    [Fact]
    public void Ok_PayloadStartingWithErrorPrefixStaysSuccessful()
    {
        // The whole point of item 1.1: payload text must never drive classification.
        var result = WorkerCallResult.Ok("Error: literal block comment content, not a failure");

        Assert.True(result.Success);
    }

    [Fact]
    public void Warnings_AreAttachedToBothShapes()
    {
        Assert.Single(WorkerCallResult.Ok("x", new[] { "w1" }).Warnings);
        Assert.Single(
            WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "e", new[] { "w1" }).Warnings);
    }

    [Fact]
    public void ToEnvelopeText_SuccessCarriesPayloadAndSeparateWarningsArray()
    {
        // A degraded-but-successful direct status read: warnings live in their own array, never
        // concatenated into the payload, and failureCategory stays null on success.
        var result = WorkerCallResult.Ok(
            "{\"isOpen\":true}",
            new[] { "Skipping device 'X' while reading hardware configuration: access denied." });

        using var envelope = JsonDocument.Parse(result.ToEnvelopeText());
        var root = envelope.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("{\"isOpen\":true}", root.GetProperty("payload").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("failureCategory").ValueKind);
        Assert.Equal(1, root.GetProperty("warnings").GetArrayLength());
        Assert.DoesNotContain("Skipping device", root.GetProperty("payload").GetString()!);
    }

    [Fact]
    public void ToEnvelopeText_KeepsFailureCategoryPrimaryEvenWhenWarningsExist()
    {
        var result = WorkerCallResult.Fail(
            WorkerFailureCategories.WorkerCrashed,
            "The write outcome is unknown. Inspect current project state before retrying.",
            new[] { "Skipping device 'X': access denied." });

        using var envelope = JsonDocument.Parse(result.ToEnvelopeText());
        var root = envelope.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.WorkerCrashed, root.GetProperty("failureCategory").GetString());
        Assert.Equal(
            "The write outcome is unknown. Inspect current project state before retrying.",
            root.GetProperty("error").GetString());
        Assert.Equal(1, root.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public void FailureRendering_DoesNotIncludeProtectedInputDataNeverPassedToTheResult()
    {
        // Simulates a credential-like value that lives only in caller-side "protected input"
        // (e.g. requestedInput/target passed to preview and audit elsewhere) and must never be
        // echoed by these renderers, since WorkerCallResult only ever carries Payload/Error/
        // FailureCategory/Warnings — it never receives, and therefore can never leak, that data.
        const string protectedTestOnlyValue = "test-only-fake-credential-do-not-use-9f3c1a";
        var result = WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "boom");

        Assert.DoesNotContain(protectedTestOnlyValue, result.ToText());
        Assert.DoesNotContain(protectedTestOnlyValue, result.ToEnvelopeText());
    }
}
