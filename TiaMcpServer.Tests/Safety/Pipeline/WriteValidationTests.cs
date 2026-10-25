using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.OperationBatches;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

[Collection(ConfiguredPayloadBudgetCollection.Name)]
public class WriteValidationTests
{
    [Fact]
    public void Compose_InvalidCatalogPreservesErrorAndSkipsLaterStages()
    {
        var result = WriteValidation.Compose(
            OperationValidation.Invalid("catalog_rejection", "The catalog rejected this item."),
            () => throw new InvalidOperationException("Budget measurement must not run."),
            () => throw new InvalidOperationException("Access validation must not run."),
            "plc_write");

        Assert.False(result.IsValid);
        Assert.Equal(new WriteToolError("catalog_rejection", "The catalog rejected this item."), result.Error);
    }

    [Theory]
    [InlineData("plc_write")]
    [InlineData("hmi_write")]
    [InlineData("network_write")]
    [InlineData("vci_write")]
    public void Compose_ProtectedCoreOverLimitRejectsBeforeAccessValidation(string toolName)
    {
        var result = WriteValidation.Compose(
            OperationValidation.Valid,
            () => StructuredOperationBatchPayloadBudget.MaxDocumentChars + 1,
            () => throw new InvalidOperationException("Access validation must not run."),
            toolName);

        Assert.False(result.IsValid);
        Assert.Equal(new WriteToolError(WorkerFailureCategories.ValidationError,
            $"The encoded operation identities leave insufficient room for the required response summaries. Use smaller {toolName} calls."), result.Error);
    }

    [Fact]
    public void Compose_ProtectedCoreAtLimitIsAdmitted()
    {
        var accessCalls = 0;
        var result = WriteValidation.Compose(
            OperationValidation.Valid,
            () => StructuredOperationBatchPayloadBudget.MaxDocumentChars,
            () => { accessCalls++; return []; },
            "plc_write");

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        Assert.Equal(1, accessCalls);
    }

    [Fact]
    public void Compose_AccessErrorsPreserveOrderAndNewlines()
    {
        var result = WriteValidation.Compose(OperationValidation.Valid, () => 0, () => ["first", "second"], "plc_write");

        Assert.False(result.IsValid);
        Assert.Equal(new WriteToolError(WorkerFailureCategories.AccessDenied, "first\nsecond"), result.Error);
    }

    [Fact]
    public void Compose_ValidCallsEachLaterStageOnce()
    {
        var stages = new List<string>();

        var result = WriteValidation.Compose(OperationValidation.Valid,
            () => { stages.Add("budget"); return 0; },
            () => { stages.Add("access"); return []; },
            "plc_write");

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
        Assert.Equal(new[] { "budget", "access" }, stages);
    }

    [Fact]
    public void Compose_UsesConfiguredDocumentLimit()
    {
        var previousItemLimit = StructuredOperationBatchPayloadBudget.MaxItemChars;
        var previousDocumentLimit = StructuredOperationBatchPayloadBudget.MaxDocumentChars;
        try
        {
            StructuredOperationBatchPayloadBudget.Configure(previousItemLimit, 300_000);

            var result = WriteValidation.Compose(OperationValidation.Valid, () => 250_000, () => [], "plc_write");
            var overflow = WriteValidation.Compose(OperationValidation.Valid, () => 300_001,
                () => throw new InvalidOperationException("Access validation must not run."), "plc_write");

            Assert.True(result.IsValid);
            Assert.Null(result.Error);
            Assert.False(overflow.IsValid);
            Assert.Equal(WorkerFailureCategories.ValidationError, overflow.Error?.Category);
        }
        finally
        {
            StructuredOperationBatchPayloadBudget.Configure(previousItemLimit, previousDocumentLimit);
        }
    }
}
