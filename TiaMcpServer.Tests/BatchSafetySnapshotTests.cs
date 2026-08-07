using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Batch;
using TiaMcpServer.OperationBatches;
using Xunit;

namespace TiaMcpServer.Tests;

public class BatchSafetySnapshotTests
{
    private static BatchOperationRequest Op(string id, string operation, Action<BatchOperationRequest>? configure = null)
    {
        var request = new BatchOperationRequest { OperationId = id, Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    [Fact]
    public void DescribeOperation_DescribesBlockUpdateWithPath()
    {
        var summary = BatchSafetySnapshot.DescribeOperation(
            Op("a", "update_block_logic", r => { r.BlockPath = "PLC_1/Main"; r.YamlContent = "x"; }));

        Assert.Contains("PLC_1/Main", summary);
    }

    [Fact]
    public void DescribeOperation_DescribesTagCreateWithNameAndTable()
    {
        var summary = BatchSafetySnapshot.DescribeOperation(
            Op("a", "create_tag", r => { r.TableName = "Inputs"; r.Name = "Start"; r.DataType = "Bool"; }));

        Assert.Contains("Start", summary);
        Assert.Contains("Inputs", summary);
    }

    [Fact]
    public void BuildTargets_PreservesOrderAndOperationIdentity()
    {
        var operations = new[]
        {
            Op("first", "create_tag_table", r => r.TableName = "T1"),
            Op("second", "delete_tag_table", r => r.TableName = "T2"),
        };

        var targets = BatchSafetySnapshot.BuildTargets(operations);

        Assert.Equal(new[] { "first", "second" }, targets.Select(t => t.OperationId).ToArray());
        Assert.Equal(new[] { "create_tag_table", "delete_tag_table" }, targets.Select(t => t.Operation).ToArray());
        Assert.Equal(BatchSafetySnapshot.DescribeOperation(operations[0]), targets[0].Summary);
    }

    [Fact]
    public void BuildTargets_IsOrderSensitive()
    {
        var a = Op("a", "create_tag_table", r => r.TableName = "T1");
        var b = Op("b", "delete_tag_table", r => r.TableName = "T2");

        var forward = BatchSafetySnapshot.BuildTargets(new[] { a, b }).Select(t => t.OperationId);
        var reverse = BatchSafetySnapshot.BuildTargets(new[] { b, a }).Select(t => t.OperationId);

        Assert.NotEqual(forward, reverse);
    }

    [Fact]
    public void CombineCurrentState_IncludesEachStateAndIsOrderSensitive()
    {
        var states = new List<OperationBatchCurrentState>
        {
            new("a", "list_tag_tables", "STATE_ONE"),
            new("b", "get_block_content", "STATE_TWO"),
        };
        var reversed = new List<OperationBatchCurrentState> { states[1], states[0] };

        var combined = BatchSafetySnapshot.CombineCurrentState(states);

        Assert.Contains("STATE_ONE", combined);
        Assert.Contains("STATE_TWO", combined);
        Assert.NotEqual(combined, BatchSafetySnapshot.CombineCurrentState(reversed));
    }

    [Fact]
    public void ResolveProjectPath_ReturnsFirstNonEmptyPath()
    {
        var operations = new[]
        {
            Op("a", "create_tag_table", r => { r.TableName = "T1"; r.ProjectPath = null; }),
            Op("b", "delete_tag_table", r => { r.TableName = "T2"; r.ProjectPath = @"C:\proj\demo.ap21"; }),
        };

        Assert.Equal(@"C:\proj\demo.ap21", BatchSafetySnapshot.ResolveProjectPath(operations));
    }

    [Fact]
    public void ResolveProjectPath_ReturnsNullWhenNoPathProvided()
    {
        var operations = new[] { Op("a", "create_tag_table", r => r.TableName = "T1") };

        Assert.Null(BatchSafetySnapshot.ResolveProjectPath(operations));
    }
}
