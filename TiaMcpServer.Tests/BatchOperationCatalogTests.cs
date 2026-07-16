using System.Collections.Generic;
using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests;

public class BatchOperationCatalogTests
{
    private static BatchOperationRequest Op(string id, string operation, Action<BatchOperationRequest>? configure = null)
    {
        var request = new BatchOperationRequest { OperationId = id, Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    [Fact]
    public void ValidateReadBatch_AcceptsKnownReadsWithRequiredFields()
    {
        var operations = new List<BatchOperationRequest>
        {
            Op("a", "browse_project_tree"),
            Op("b", "get_block_content", r => r.BlockPath = "PLC_1/Main"),
            Op("c", "search_equipment_catalog", r => r.Query = "CPU"),
        };

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateReadBatch_RejectsEmptyBatch()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new List<BatchOperationRequest>());

        Assert.False(result.IsValid);
        Assert.Contains("at least one", result.Error);
    }

    [Fact]
    public void ValidateReadBatch_RejectsNullBatch()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(null);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateReadBatch_RejectsBatchOverFiftyItems()
    {
        var operations = new List<BatchOperationRequest>();
        for (var i = 0; i < BatchOperationCatalog.MaxBatchSize + 1; i++)
        {
            operations.Add(Op($"id{i}", "browse_project_tree"));
        }

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.False(result.IsValid);
        Assert.Contains("50", result.Error);
    }

    [Fact]
    public void ValidateReadBatch_RejectsUnknownOperation()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[] { Op("a", "teleport_plc") });

        Assert.False(result.IsValid);
        Assert.Contains("teleport_plc", result.Error);
    }

    [Fact]
    public void ValidateReadBatch_RejectsWriteOperation()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(
            new[] { Op("a", "update_block_logic", r => { r.BlockPath = "Main"; r.YamlContent = "x"; }) });

        Assert.False(result.IsValid);
        Assert.Contains("update_block_logic", result.Error);
        Assert.Contains("write", result.Error);
    }

    [Fact]
    public void ValidateReadBatch_RejectsDuplicateOperationId()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(
            new[] { Op("dup", "browse_project_tree"), Op("dup", "read_hardware_config") });

        Assert.False(result.IsValid);
        Assert.Contains("dup", result.Error);
    }

    [Fact]
    public void ValidateReadBatch_RejectsMissingOperationId()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[] { Op("", "browse_project_tree") });

        Assert.False(result.IsValid);
        Assert.Contains("operationId", result.Error);
    }

    [Fact]
    public void ValidateReadBatch_RejectsMissingRequiredField()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[] { Op("a", "get_block_content") });

        Assert.False(result.IsValid);
        Assert.Contains("blockPath", result.Error);
    }

    [Fact]
    public void ValidateWriteBatch_AcceptsKnownDataWrites()
    {
        var operations = new List<BatchOperationRequest>
        {
            Op("a", "create_tag", r => { r.TableName = "Inputs"; r.Name = "Start"; r.DataType = "Bool"; }),
            Op("b", "update_block_logic", r => { r.BlockPath = "Main"; r.YamlContent = "name: Main"; }),
            Op("c", "configure_network_device", r => r.DeviceName = "PLC_1"),
        };

        var result = BatchOperationCatalog.ValidateWriteBatch(operations);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateWriteBatch_RejectsReadOperation()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(
            new[] { Op("a", "get_block_content", r => r.BlockPath = "Main") });

        Assert.False(result.IsValid);
        Assert.Contains("get_block_content", result.Error);
        Assert.Contains("read", result.Error);
    }

    [Fact]
    public void ValidateWriteBatch_RejectsProjectLifecycleOperation()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(
            new[] { Op("a", "close_project") });

        Assert.False(result.IsValid);
        Assert.Contains("close_project", result.Error);
    }

    [Fact]
    public void ValidateWriteBatch_RejectsMissingRequiredField()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(
            new[] { Op("a", "create_tag", r => { r.TableName = "Inputs"; r.Name = "Start"; }) });

        Assert.False(result.IsValid);
        Assert.Contains("dataType", result.Error);
    }

    [Fact]
    public void ValidateWriteBatch_RejectsUnknownOperation()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { Op("a", "frobnicate") });

        Assert.False(result.IsValid);
        Assert.Contains("frobnicate", result.Error);
    }

    [Fact]
    public void ValidateWriteBatch_RejectsMixedProjectPaths()
    {
        var operations = new[]
        {
            Op("a", "create_tag_table", r => { r.TableName = "T1"; r.ProjectPath = @"C:\a.ap21"; }),
            Op("b", "delete_tag_table", r => { r.TableName = "T2"; r.ProjectPath = @"C:\b.ap21"; }),
        };

        var result = BatchOperationCatalog.ValidateWriteBatch(operations);

        Assert.False(result.IsValid);
        Assert.Contains("same project", result.Error);
    }

    [Fact]
    public void ValidateWriteBatch_AllowsSameProjectPathOnEveryItem()
    {
        var operations = new[]
        {
            Op("a", "create_tag_table", r => { r.TableName = "T1"; r.ProjectPath = @"C:\a.ap21"; }),
            Op("b", "delete_tag_table", r => { r.TableName = "T2"; r.ProjectPath = @"C:\a.ap21"; }),
        };

        var result = BatchOperationCatalog.ValidateWriteBatch(operations);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void AllWriteOperations_AcceptAFullyPopulatedRequest()
    {
        foreach (var operation in BatchOperationCatalog.WriteOperationNames)
        {
            var result = BatchOperationCatalog.ValidateWriteBatch(new[] { FullyPopulated("id", operation) });
            Assert.True(result.IsValid, $"{operation}: {result.Error}");
        }
    }

    [Fact]
    public void AllReadOperations_AcceptAFullyPopulatedRequest()
    {
        foreach (var operation in BatchOperationCatalog.ReadOperationNames)
        {
            var result = BatchOperationCatalog.ValidateReadBatch(new[] { FullyPopulated("id", operation) });
            Assert.True(result.IsValid, $"{operation}: {result.Error}");
        }
    }

    [Fact]
    public void ValidateReadBatch_UnknownOperationErrorListsValidReadOperations()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[] { Op("a", "teleport_plc") });

        Assert.False(result.IsValid);
        Assert.Contains("Valid read operations", result.Error);
        Assert.Contains("browse_project_tree", result.Error);
        Assert.Contains("get_block_content", result.Error);
    }

    [Fact]
    public void ValidateWriteBatch_UnknownOperationErrorListsValidWriteOperations()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { Op("a", "frobnicate") });

        Assert.False(result.IsValid);
        Assert.Contains("Valid write operations", result.Error);
        Assert.Contains("update_block_logic", result.Error);
        Assert.Contains("create_tag", result.Error);
    }

    [Fact]
    public void ValidateWriteBatch_ReportsAllInvalidItemsAtOnce()
    {
        var operations = new[]
        {
            Op("a", "creat_tag"),
            Op("b", "create_tag", r => r.TableName = "Inputs"),
            Op("c", "get_block_content", r => r.BlockPath = "Main"),
        };

        var result = BatchOperationCatalog.ValidateWriteBatch(operations);

        Assert.False(result.IsValid);
        Assert.Contains("creat_tag", result.Error);
        Assert.Contains("dataType", result.Error);
        Assert.Contains("get_block_content", result.Error);
    }

    [Fact]
    public void Validate_RejectsDepthAndStartPathOnNonTreeOperations()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "list_tag_tables", Depth = 2 },
            new BatchOperationRequest { OperationId = "b", Operation = "get_project_status", StartPath = "PLC_1" },
        };

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.False(result.IsValid);
        Assert.Contains("'depth' is only valid for browse_project_tree", result.Error);
        Assert.Contains("'startPath' is only valid for browse_project_tree", result.Error);
        Assert.Contains("operationId 'a'", result.Error);
        Assert.Contains("operationId 'b'", result.Error);
    }

    [Fact]
    public void Validate_RejectsMaxResultsOnUnsupportedOperations()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "browse_project_tree", MaxResults = 10 },
        };

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.False(result.IsValid);
        Assert.Contains("'maxResults' is only valid for search_equipment_catalog and read_cross_references", result.Error);
    }

    [Fact]
    public void Validate_RejectsOutOfRangeBounds()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "browse_project_tree", Depth = 0 },
            new BatchOperationRequest { OperationId = "b", Operation = "search_equipment_catalog", Query = "cpu", MaxResults = 0 },
        };

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.False(result.IsValid);
        Assert.Contains("'depth' must be 1 or greater", result.Error);
        Assert.Contains("'maxResults' must be 1 or greater", result.Error);
    }

    [Fact]
    public void Validate_AcceptsBoundsOnTheirOperations()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "browse_project_tree", Depth = 2, StartPath = "PLC_1/Blocks" },
            new BatchOperationRequest { OperationId = "b", Operation = "search_equipment_catalog", Query = "cpu", MaxResults = 10 },
            new BatchOperationRequest { OperationId = "c", Operation = "read_cross_references", MaxResults = 100 },
        };

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.True(result.IsValid, result.Error);
    }

    private static BatchOperationRequest FullyPopulated(string id, string operation) => new()
    {
        OperationId = id,
        Operation = operation,
        BlockPath = "PLC_1/Main",
        YamlContent = "name: Main",
        BlockType = "FB",
        Query = "CPU",
        TableName = "Inputs",
        Name = "Item",
        DataType = "Bool",
        Value = "1",
        TypeIdentifier = "OrderNumber:X/V1.0",
        DeviceName = "PLC_1",
    };
}
