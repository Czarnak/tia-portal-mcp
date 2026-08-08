using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public class TypeOperationCatalogTests
{
    private static BatchOperationRequest ReadOp() => new()
    {
        OperationId = "r1",
        Operation = "get_type_content",
        TypePath = "PLC_1/Types/AnalogInputSettings",
    };

    private static BatchOperationRequest WriteOp() => new()
    {
        OperationId = "w1",
        Operation = "update_type_content",
        TypePath = "PLC_1/Types/AnalogInputSettings",
        SourceContent = "TYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n",
    };

    [Fact]
    public void get_type_content_is_registered_as_a_read()
    {
        Assert.True(BatchOperationCatalog.TryGetSpec("get_type_content", out var spec));
        Assert.Equal(BatchOperationCategory.Read, spec!.Category);
        Assert.Contains("get_type_content", BatchOperationCatalog.ReadOperationNames);
    }

    [Fact]
    public void update_type_content_is_registered_as_a_write()
    {
        Assert.True(BatchOperationCatalog.TryGetSpec("update_type_content", out var spec));
        Assert.Equal(BatchOperationCategory.Write, spec!.Category);
        Assert.Contains("update_type_content", BatchOperationCatalog.WriteOperationNames);
    }

    [Fact]
    public void get_type_content_accepts_a_type_path()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[] { ReadOp() });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void get_type_content_accepts_an_optional_format()
    {
        var op = ReadOp();
        op.Format = "xml";

        var result = BatchOperationCatalog.ValidateReadBatch(new[] { op });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void get_type_content_requires_a_type_path()
    {
        var op = ReadOp();
        op.TypePath = null;

        var result = BatchOperationCatalog.ValidateReadBatch(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains("typePath", result.Error);
    }

    [Fact]
    public void get_type_content_rejects_a_block_path()
    {
        var op = ReadOp();
        op.BlockPath = "PLC_1/Blocks/Main";

        var result = BatchOperationCatalog.ValidateReadBatch(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains("blockPath", result.Error);
    }

    [Fact]
    public void update_type_content_requires_both_type_path_and_source_content()
    {
        var op = WriteOp();
        op.SourceContent = null;

        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains("sourceContent", result.Error);
    }

    [Fact]
    public void update_type_content_is_valid_with_both_required_fields()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { WriteOp() });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void get_type_content_is_rejected_inside_a_write_batch()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { ReadOp() });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void update_type_content_is_rejected_inside_a_read_batch()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[] { WriteOp() });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void update_type_content_has_a_preview_description_naming_the_type()
    {
        var summary = BatchSafetySnapshot.DescribeOperation(WriteOp());

        Assert.Equal("Update PLC data type 'PLC_1/Types/AnalogInputSettings'.", summary);
    }

    [Fact]
    public void get_block_content_still_accepts_an_optional_format()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "r2",
            Operation = "get_block_content",
            BlockPath = "PLC_1/Blocks/InputValues_DB",
            Format = "source",
        };

        var result = BatchOperationCatalog.ValidateReadBatch(new[] { op });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void update_block_logic_still_accepts_an_optional_format()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "w2",
            Operation = "update_block_logic",
            BlockPath = "PLC_1/Blocks/InputValues_DB",
            YamlContent = "--- FILE: x.xml ---\n<Document />",
            Format = "xml",
        };

        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { op });

        Assert.True(result.IsValid, result.Error);
    }
}