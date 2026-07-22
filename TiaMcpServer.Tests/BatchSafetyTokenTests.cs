using System.Linq;
using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Safety;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Exercises the batch-level safety token end to end through the real <see cref="WriteSafetyService"/>,
/// using the exact ordered target/input/combined-state snapshots that apply_write_batch binds to.
/// No worker is needed because the current-state strings are supplied directly.
/// </summary>
public class BatchSafetyTokenTests
{
    private const string ApplyToolName = "apply_write_batch";

    private static BatchOperationRequest Op(string id, string operation, Action<BatchOperationRequest>? configure = null)
    {
        var request = new BatchOperationRequest { OperationId = id, Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    private static string CreatePreview(WriteSafetyService service, BatchOperationRequest[] ops, string[] states)
    {
        var targets = BatchSafetySnapshot.BuildTargets(ops);
        var combined = BatchSafetySnapshot.CombineCurrentState(
            ops.Select((o, i) => new BatchCurrentState(o.OperationId, o.Operation, states[i])).ToList());
        var project = BatchSafetySnapshot.ResolveProjectPath(ops);
        var json = service.CreatePreview(ApplyToolName, project, targets, "summary", ops, combined);
        return JsonDocument.Parse(json).RootElement.GetProperty("safetyToken").GetString()!;
    }

    private static WriteSafetyValidationResult Consume(
        WriteSafetyService service,
        string token,
        BatchOperationRequest[] ops,
        string[] states,
        string toolName = ApplyToolName)
    {
        var targets = BatchSafetySnapshot.BuildTargets(ops);
        var combined = BatchSafetySnapshot.CombineCurrentState(
            ops.Select((o, i) => new BatchCurrentState(o.OperationId, o.Operation, states[i])).ToList());
        var project = BatchSafetySnapshot.ResolveProjectPath(ops);
        return service.ValidateAndConsume(token, toolName, project, targets, ops, combined);
    }

    private static (BatchOperationRequest[] ops, string[] states) TwoItemBatch()
    {
        var ops = new[]
        {
            Op("a", "create_tag", r => { r.TableName = "Inputs"; r.Name = "Start"; r.DataType = "Bool"; }),
            Op("b", "update_block_logic", r => { r.BlockPath = "Main"; r.YamlContent = "name: Main"; }),
        };
        return (ops, new[] { "TAGS_STATE", "BLOCK_STATE" });
    }

    [Fact]
    public void UnchangedBatchValidates()
    {
        using var audit = new TempAuditDirectory();
        var service = audit.CreateSafety();
        var (ops, states) = TwoItemBatch();
        var token = CreatePreview(service, ops, states);

        var result = Consume(service, token, ops, states);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ReorderedOperationsAreRejected()
    {
        using var audit = new TempAuditDirectory();
        var service = audit.CreateSafety();
        var (ops, states) = TwoItemBatch();
        var token = CreatePreview(service, ops, states);

        var reordered = new[] { ops[1], ops[0] };
        var reorderedStates = new[] { states[1], states[0] };
        var result = Consume(service, token, reordered, reorderedStates);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ChangedInputIsRejected()
    {
        using var audit = new TempAuditDirectory();
        var service = audit.CreateSafety();
        var (ops, states) = TwoItemBatch();
        var token = CreatePreview(service, ops, states);

        var tampered = new[]
        {
            ops[0],
            Op("b", "update_block_logic", r => { r.BlockPath = "Main"; r.YamlContent = "name: Tampered"; }),
        };
        var result = Consume(service, token, tampered, states);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ChangedCurrentStateIsRejected()
    {
        using var audit = new TempAuditDirectory();
        var service = audit.CreateSafety();
        var (ops, states) = TwoItemBatch();
        var token = CreatePreview(service, ops, states);

        var result = Consume(service, token, ops, new[] { "TAGS_STATE_CHANGED", "BLOCK_STATE" });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void DifferentProjectPathIsRejected()
    {
        using var audit = new TempAuditDirectory();
        var service = audit.CreateSafety();
        var (ops, states) = TwoItemBatch();
        var token = CreatePreview(service, ops, states);

        var rebound = new[]
        {
            Op("a", "create_tag", r => { r.TableName = "Inputs"; r.Name = "Start"; r.DataType = "Bool"; r.ProjectPath = @"C:\other.ap21"; }),
            ops[1],
        };
        var result = Consume(service, token, rebound, states);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void WrongToolNameIsRejected()
    {
        using var audit = new TempAuditDirectory();
        var service = audit.CreateSafety();
        var (ops, states) = TwoItemBatch();
        var token = CreatePreview(service, ops, states);

        var result = Consume(service, token, ops, states, toolName: "apply_read_batch");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void TokenIsSingleUse()
    {
        using var audit = new TempAuditDirectory();
        var service = audit.CreateSafety();
        var (ops, states) = TwoItemBatch();
        var token = CreatePreview(service, ops, states);

        Assert.True(Consume(service, token, ops, states).IsValid);
        Assert.False(Consume(service, token, ops, states).IsValid);
    }

    [Fact]
    public void UnknownTokenIsRejected()
    {
        using var audit = new TempAuditDirectory();
        var service = audit.CreateSafety();
        var (ops, states) = TwoItemBatch();

        var result = Consume(service, "never-issued", ops, states);

        Assert.False(result.IsValid);
    }
}
