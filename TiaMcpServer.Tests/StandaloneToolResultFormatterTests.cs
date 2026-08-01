using System.Text.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class StandaloneToolResultFormatterTests
{
    [Fact]
    public void OversizedSuccess_IsCappedWithHintAndKeepsWarnings()
    {
        var result = WorkerCallResult.Ok(
            new string('x', OperationBatchPayloadBudget.MaxItemChars + 100),
            new[] { "keep this warning" });

        var text = StandaloneToolResultFormatter.Format(
            result,
            "Narrow with depth or startPath.");

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var payload = root.GetProperty("payload").GetString()!;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(OperationBatchPayloadBudget.MaxItemChars, payload.Length);
        Assert.Contains("[TRUNCATED", payload);
        Assert.Contains("depth or startPath", payload);
        Assert.Equal(
            "keep this warning",
            root.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public void Failure_IsNotRewrittenOrTruncated()
    {
        var result = WorkerCallResult.Fail(
            WorkerFailureCategories.ValidationError,
            "invalid input",
            new[] { "keep this warning" });

        var text = StandaloneToolResultFormatter.Format(result, "unused hint");

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(string.Empty, root.GetProperty("payload").GetString());
        Assert.Equal(
            WorkerFailureCategories.ValidationError,
            root.GetProperty("failureCategory").GetString());
        Assert.Equal("invalid input", root.GetProperty("error").GetString());
        Assert.Equal(
            "keep this warning",
            root.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public void OversizedSuccess_WithOversizedHint_RemainsCapped()
    {
        var result = WorkerCallResult.Ok(
            new string('x', OperationBatchPayloadBudget.MaxItemChars + 100));

        var text = StandaloneToolResultFormatter.Format(
            result,
            new string('h', OperationBatchPayloadBudget.MaxItemChars * 2));

        using var document = JsonDocument.Parse(text);
        var payload = document.RootElement.GetProperty("payload").GetString()!;

        Assert.Equal(OperationBatchPayloadBudget.MaxItemChars, payload.Length);
        Assert.Contains("[TRUNCATED", payload);
    }
}
