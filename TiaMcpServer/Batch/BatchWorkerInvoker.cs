using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Batch;

/// <summary>
/// Maps validated batch items to existing <see cref="OpennessWorkerClient"/> calls. This is the
/// only worker-coupled part of the batch layer; orchestration and validation live elsewhere.
/// Required fields are guaranteed present by <see cref="BatchOperationCatalog"/> before this runs.
/// </summary>
public static class BatchWorkerInvoker
{
    /// <summary>Reads the current state a write item's safety token binds to.</summary>
    public static Task<WorkerCallResult> ReadCurrentStateAsync(OpennessWorkerClient client, BatchOperationRequest op) => op.Operation switch
    {
        // The safety token binds to the block as the WRITE will see it, so the current-state read
        // must use the write item's own format. Reading xml while a format=source write goes
        // through the external-source pipeline leaves the token blind to everything a .db source
        // carries but Simatic ML does not — S7_Optimized_Access, block and member comments, an
        // attribute-only edit. A concurrent TIA Portal edit of any of those would keep the token's
        // state hash matching, so the token would be accepted and the edit silently overwritten,
        // which is the exact thing the token exists to prevent.
        // NormalizeFormat can throw for an invalid format; WithValidatedFormat converts that into
        // a failed result instead of letting it propagate out of the batch loop (see InvokeAsync's
        // format-bearing arms for the same treatment).
        "update_block_logic" => WithValidatedFormat(
            () => NormalizeFormat(op),
            format => client.GetBlockContentAsync(op.BlockPath!, op.ProjectPath, format)),
        // The safety token binds to the type's current exported source, so an edit made inside
        // TIA Portal between preview and apply invalidates the token, exactly like update_block_logic.
        "update_type_content" => WithValidatedFormat(
            () => NormalizeFormat(op),
            format => client.GetTypeContentAsync(op.TypePath!, format, op.ProjectPath)),
        "update_tag" => ReadUpdateTagCurrentStateAsync(client, op),
        "create_tag_table" or "delete_tag_table"
            or "create_tag" or "delete_tag"
            or "create_user_constant" or "update_user_constant" or "delete_user_constant"
            => client.ListTagTablesAsync(op.PlcName, op.ProjectPath),
        "create_block" or "create_block_group" or "delete_block_group"
            => client.BrowseProjectTreeAsync(op.ProjectPath),
        // delete_block declares no 'format' field (see BatchOperationCatalog), so there is no
        // caller format to honour here and the worker's default export is the right binding.
        "delete_block"
            => client.GetBlockContentAsync(op.BlockPath!, op.ProjectPath),
        "start_plc" or "stop_plc"
            => client.GetProjectStatusAsync(op.ProjectPath),
        _ => Task.FromResult(WorkerCallResult.Fail(
            WorkerFailureCategories.ValidationError,
            $"Unsupported batch write operation '{op.Operation}'.")),
    };

    private static async Task<WorkerCallResult> ReadUpdateTagCurrentStateAsync(
        OpennessWorkerClient client,
        BatchOperationRequest op)
    {
        var strict = await client.ReadUpdateTagSafetySnapshotAsync(
            op.PlcName,
            op.TableName!,
            op.FolderPath,
            op.Name!,
            op.ProjectPath).ConfigureAwait(false);
        if (!strict.Success)
        {
            return strict;
        }

        TagUpdateSafetySnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<TagUpdateSafetySnapshot>(strict.Payload);
        }
        catch (JsonException ex)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError,
                $"Could not decode the update_tag safety snapshot. {ex.Message}");
        }

        if (snapshot is null)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError,
                "Could not decode the update_tag safety snapshot.");
        }

        var unavailableFlag = TagUpdateSafetyCurrentState.ValidateRequestedExternalFlags(op, snapshot);
        if (unavailableFlag is not null)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError,
                $"The current tag does not expose requested flag '{unavailableFlag}'.");
        }

        var broad = await client.ListTagTablesAsync(op.PlcName, op.ProjectPath).ConfigureAwait(false);
        if (!broad.Success)
        {
            return broad;
        }

        return WorkerCallResult.Ok(
            TagUpdateSafetyCurrentState.Compose(snapshot, broad.Payload),
            broad.Warnings) with
        {
            ResolvedProjectPath = broad.ResolvedProjectPath,
            SessionIdentity = broad.SessionIdentity,
        };
    }

    /// <summary>Executes a single read or write item against the worker.</summary>
    public static Task<WorkerCallResult> InvokeAsync(OpennessWorkerClient client, BatchOperationRequest op) => op.Operation switch
    {
        // Reads
        "read_cross_references" => client.ReadCrossReferencesAsync(op.ProjectPath, op.PlcName, op.Filter, op.MaxResults),
        "get_block_content" => InvokeGetBlockContent(client, op),
        "list_tag_tables" => client.ListTagTablesAsync(op.PlcName, op.ProjectPath),
        "get_type_content" => InvokeGetTypeContent(client, op),

        // Data writes
        "update_block_logic" => InvokeUpdateBlockLogic(client, op),
        "create_tag_table" => client.CreateTagTableAsync(op.PlcName, op.TableName!, op.FolderPath, op.ProjectPath),
        "delete_tag_table" => client.DeleteTagTableAsync(op.PlcName, op.TableName!, op.FolderPath, op.ProjectPath),
        "create_tag" => client.CreateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType!, op.LogicalAddress, op.ProjectPath),
        "update_tag" => client.UpdateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.NewName, op.DataType, op.LogicalAddress, op.ExternalAccessible, op.ExternalVisible, op.ExternalWritable, op.IsSafety, op.ProjectPath),
        "delete_tag" => client.DeleteTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.ProjectPath),
        "create_user_constant" => client.CreateUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType!, op.Value!, op.ProjectPath),
        "update_user_constant" => client.UpdateUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.DataType, op.Value, op.ProjectPath),
        "delete_user_constant" => client.DeleteUserConstantAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.ProjectPath),
        "create_block" => client.CreateBlockAsync(op.BlockPath!, op.BlockType!, op.Language, op.ObEventClass, op.ProjectPath),
        "delete_block" => client.DeleteBlockAsync(op.BlockPath!, op.ProjectPath),
        "create_block_group" => client.CreateBlockGroupAsync(op.BlockPath!, op.ProjectPath),
        "delete_block_group" => client.DeleteBlockGroupAsync(op.BlockPath!, op.ProjectPath),
        "start_plc" => client.StartPlcAsync(op.PlcName, op.ProjectPath),
        "stop_plc" => client.StopPlcAsync(op.PlcName, op.ProjectPath),
        "update_type_content" => InvokeUpdateTypeContent(client, op),

        _ => Task.FromResult(WorkerCallResult.Fail(
            WorkerFailureCategories.ValidationError,
            $"Unsupported batch operation '{op.Operation}'.")),
    };

    /// <summary>
    /// Builds the <see cref="WorkerRequest"/> a batch item would send, including format
    /// normalization/validation, without touching the worker. This is the seam that makes
    /// request construction — and an invalid format's rejection before any session binds —
    /// testable without a worker process, exactly as <see cref="BatchSafetySnapshot"/> made
    /// snapshot construction testable without one. Only the four format-bearing operations
    /// (get_block_content, update_block_logic, get_type_content, update_type_content) populate
    /// operation-specific fields; the invoke arms above are the only production callers.
    /// </summary>
    public static WorkerRequest BuildRequest(BatchOperationRequest op)
    {
        var request = new WorkerRequest
        {
            Method = op.Operation,
            ProjectPath = op.ProjectPath,
        };

        switch (op.Operation)
        {
            case "get_block_content":
                request.BlockPath = op.BlockPath;
                request.Format = NormalizeFormat(op);
                request.WithDependencies = op.WithDependencies;
                break;
            case "update_block_logic":
                request.BlockPath = op.BlockPath;
                request.YamlContent = op.YamlContent;
                request.Format = NormalizeFormat(op);
                request.AllowTiaConfirmations = true;
                break;
            case "get_type_content":
                request.TypePath = op.TypePath;
                request.Format = NormalizeFormat(op);
                request.WithDependencies = op.WithDependencies;
                break;
            case "update_type_content":
                request.TypePath = op.TypePath;
                request.SourceContent = op.SourceContent;
                request.Format = NormalizeFormat(op);
                request.AllowTiaConfirmations = true;
                break;
        }

        return request;
    }

    private static string NormalizeFormat(BatchOperationRequest op)
    {
        var fallback = op.Operation is "get_type_content" or "update_type_content"
            ? SourceFormatNames.Source
            : SourceFormatNames.Xml;

        if (!SourceFormatNames.TryNormalize(op.Format, fallback, out var normalized, out var error))
        {
            throw new ArgumentException(error, nameof(op));
        }

        return normalized;
    }

    private static Task<WorkerCallResult> InvokeGetBlockContent(OpennessWorkerClient client, BatchOperationRequest op)
        => WithValidatedFormat(
            () => BuildRequest(op),
            request => client.GetBlockContentAsync(
                request.BlockPath!, op.ProjectPath, request.Format, request.WithDependencies));

    private static Task<WorkerCallResult> InvokeUpdateBlockLogic(OpennessWorkerClient client, BatchOperationRequest op)
        => WithValidatedFormat(
            () => BuildRequest(op),
            request => client.UpdateBlockLogicAsync(request.BlockPath!, request.YamlContent!, op.ProjectPath, request.Format));

    private static Task<WorkerCallResult> InvokeGetTypeContent(OpennessWorkerClient client, BatchOperationRequest op)
        => WithValidatedFormat(
            () => BuildRequest(op),
            request => client.GetTypeContentAsync(
                request.TypePath!, request.Format, op.ProjectPath, request.WithDependencies));

    private static Task<WorkerCallResult> InvokeUpdateTypeContent(OpennessWorkerClient client, BatchOperationRequest op)
        => WithValidatedFormat(
            () => BuildRequest(op),
            request => client.UpdateTypeContentAsync(request.TypePath!, request.SourceContent!, request.Format, op.ProjectPath));

    /// <summary>
    /// Runs a format-validating builder (<see cref="BuildRequest"/> or <see cref="NormalizeFormat"/>,
    /// both of which throw <see cref="ArgumentException"/> for an invalid format) and, on success,
    /// hands its result to the worker call. A batch item with a bad format must fail only that item —
    /// per BatchTools.cs's documented contract that one failing item never stops the others — so the
    /// exception is caught here and converted into the same graceful validation_error result every
    /// other rejected-before-the-worker case in this class already returns (compare the catalog-miss
    /// fallback arms above and ReadCrossReferencesAsync's filter validation).
    /// </summary>
    private static Task<WorkerCallResult> WithValidatedFormat<T>(Func<T> build, Func<T, Task<WorkerCallResult>> invoke)
    {
        T value;
        try
        {
            value = build();
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, ex.Message));
        }

        return invoke(value);
    }

}
