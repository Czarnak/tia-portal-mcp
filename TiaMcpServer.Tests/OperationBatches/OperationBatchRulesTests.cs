using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.OperationBatches;

/// <summary>The shared count and per-item envelope rules every batch catalog and the write entry guard use.</summary>
public class OperationBatchRulesTests
{
    [Theory]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(200000)]
    public void CheckItem_BoundsOperationNameWithoutEcho(int length)
    {
        var name = new string('n', length);
        var item = Item("id");
        item.Operation = name;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var error = OperationBatchRules.CheckItem(item, seen);

        if (length <= 256)
        {
            Assert.Null(error);
            Assert.Contains("id", seen);
        }
        else
        {
            Assert.Equal("Each operation name must be at most 256 characters.", error);
            Assert.DoesNotContain(name, error);
            Assert.Empty(seen);
        }
    }

    private const string LengthMessage = "Each operationId must be at most 256 characters.";

    private static PlcOperationRequest Item(string id) => new() { OperationId = id, Operation = "list_tag_tables" };

    [Theory]
    [InlineData(0, "Batch must contain at least one operation.")]
    [InlineData(1, null)]
    [InlineData(50, null)]
    [InlineData(51, "Batch exceeds the maximum of 50 operations (received 51).")]
    public void CheckCount_UsesSharedWording(int count, string? expected)
    {
        Assert.Equal(expected, OperationBatchRules.CheckCount(count));
    }

    [Fact]
    public void CheckItem_RejectsNullBlankAndDuplicate()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        Assert.Equal("Batch contains a null operation.", OperationBatchRules.CheckItem(null, seen));
        Assert.Equal("Each operation requires a unique operationId.", OperationBatchRules.CheckItem(Item(""), seen));
        Assert.Equal("Each operation requires a unique operationId.", OperationBatchRules.CheckItem(Item("  "), seen));
        Assert.Null(OperationBatchRules.CheckItem(Item("x"), seen));
        Assert.Equal("Duplicate operationId 'x'.", OperationBatchRules.CheckItem(Item("x"), seen));
    }

    [Fact]
    public void CheckItem_BoundsOperationIdWithoutEcho()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var longest = new string('a', 256);
        var oversized = new string('b', 257);

        Assert.Null(OperationBatchRules.CheckItem(Item(longest), seen));
        var message = OperationBatchRules.CheckItem(Item(oversized), seen);

        Assert.Equal(LengthMessage, message);
        Assert.DoesNotContain(oversized, message);
    }

    [Fact]
    public void OperationValidation_InvalidCarriesCategoryAndError()
    {
        var validation = OperationValidation.Invalid("invalid_selector", "m");

        Assert.False(validation.IsValid);
        Assert.Equal("invalid_selector", validation.Category);
        Assert.Equal("m", validation.Error);
    }

    [Fact]
    public void CheckItem_OversizedIdNeverReachesDuplicateCheck()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var oversized = new string('c', 300);

        Assert.Equal(LengthMessage, OperationBatchRules.CheckItem(Item(oversized), seen));
        Assert.Equal(LengthMessage, OperationBatchRules.CheckItem(Item(oversized), seen));
        Assert.Empty(seen);
    }
}
