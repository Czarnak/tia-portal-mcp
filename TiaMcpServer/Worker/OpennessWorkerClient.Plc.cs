using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Worker;

public partial class OpennessWorkerClient
{
    public Task<WorkerCallResult> GetBlockContentAsync(
        string blockPath,
        string? format = null,
        bool? withDependencies = null)
    {
        return SendBoundProjectRequestAsync(
            "get_block_content",
            null,
            request =>
            {
                request.BlockPath = blockPath;
                request.Format = format;
                request.WithDependencies = withDependencies;
            },
            string.Empty);
    }

    public async Task<WorkerCallResult> UpdateBlockLogicAsync(
        string blockPath,
        string yamlContent,
        string? format = null,
        string? expectedContentHash = null)
    {
        var hasNormalizedFormat = SourceFormatNames.TryNormalize(
            format,
            SourceFormatNames.Xml,
            out var normalizedFormat,
            out _);
        var requestFormat = hasNormalizedFormat ? normalizedFormat : format;
        var validationFormat = hasNormalizedFormat ? normalizedFormat : null;

        var result = await SendBoundProjectRequestAsync(
            "update_block_logic",
            null,
            request =>
            {
                request.BlockPath = blockPath;
                request.Content = yamlContent;
                request.ExpectedContentHash = expectedContentHash;
                request.Format = requestFormat;
            },
            string.Empty).ConfigureAwait(false);

        if (result.DispatchState != WorkerDispatchState.Sent)
        {
            return result with
            {
                BlockImportOutcome = BlockImportOutcomeSynthesizer.Synthesize(
                    result.DispatchState,
                    validationFormat)
            };
        }

        if (BlockImportOutcomeValidator.Validate(result.BlockImportOutcome, validationFormat))
        {
            return result;
        }

        return WorkerCallResult.Fail(
            WorkerFailureCategories.ProtocolError,
            "The TIA Openness worker returned invalid block-import outcome evidence.",
            result.Warnings) with
        {
            ResolvedProjectPath = result.ResolvedProjectPath,
            SessionIdentity = result.SessionIdentity,
            DispatchState = WorkerDispatchState.Sent,
            BlockImportOutcome = BlockImportOutcomeSynthesizer.Synthesize(
                WorkerDispatchState.Sent,
                validationFormat)
        };
    }

    /// <summary>
    /// Reads a PLC data type's exported source. Mirrors <see cref="GetBlockContentAsync"/>: same
    /// <see cref="SendBoundProjectRequestAsync"/> construction, same result handling, no bespoke logic.
    /// </summary>
    public Task<WorkerCallResult> GetTypeContentAsync(
        string typePath,
        string? format,
        bool? withDependencies = null)
    {
        return SendBoundProjectRequestAsync(
            "get_type_content",
            null,
            request =>
            {
                request.TypePath = typePath;
                request.Format = format;
                request.WithDependencies = withDependencies;
            },
            string.Empty);
    }

    /// <summary>
    /// Writes a PLC data type's exported source. Mirrors <see cref="UpdateBlockLogicAsync"/>: same
    /// <see cref="SendBoundProjectRequestAsync"/> construction as
    /// its sibling, no bespoke logic.
    /// </summary>
    public Task<WorkerCallResult> UpdateTypeContentAsync(
        string typePath,
        string sourceContent,
        string? format,
        string? expectedContentHash = null)
    {
        return SendBoundProjectRequestAsync(
            "update_type_content",
            null,
            request =>
            {
                request.TypePath = typePath;
                request.Content = sourceContent;
                request.ExpectedContentHash = expectedContentHash;
                request.Format = format;
            },
            string.Empty);
    }

    public Task<WorkerCallResult> ListTagTablesAsync(
        string? plcName,
        string? tableName = null,
        string? folderPath = null)
    {
        return SendBoundProjectRequestAsync(
            "list_tag_tables",
            null,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
            },
            "[]");
    }

    public Task<WorkerCallResult> CompileCheckAsync(string? blockPath, string? plcName)
    {
        return SendBoundProjectRequestAsync(
            "compile_check",
            null,
            request =>
            {
                request.BlockPath = blockPath;
                request.PlcName = plcName;
            },
            "{}");
    }

    public Task<WorkerCallResult> CreateTagTableAsync(
        string? plcName,
        string tableName,
        string? folderPath)
    {
        return SendBoundProjectRequestAsync(
            "create_tag_table",
            null,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
            },
            "{}");
    }

    public Task<WorkerCallResult> DeleteTagTableAsync(
        string? plcName,
        string tableName,
        string? folderPath)
    {
        return SendBoundProjectRequestAsync(
            "delete_tag_table",
            null,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
            },
            "{}");
    }

    public Task<WorkerCallResult> CreateTagAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string dataType,
        string? logicalAddress)
    {
        return SendBoundProjectRequestAsync(
            "create_tag",
            null,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Name = name;
                request.DataType = dataType;
                request.LogicalAddress = logicalAddress;
            },
            "{}");
    }

    public Task<WorkerCallResult> UpdateTagAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string? newName,
        string? dataType,
        string? logicalAddress,
        bool? externalAccessible,
        bool? externalVisible,
        bool? externalWritable)
    {
        return SendBoundProjectRequestAsync(
            "update_tag",
            null,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Name = name;
                request.NewName = newName;
                request.DataType = dataType;
                request.LogicalAddress = logicalAddress;
                request.ExternalAccessible = externalAccessible;
                request.ExternalVisible = externalVisible;
                request.ExternalWritable = externalWritable;
            },
            "{}");
    }

    public Task<WorkerCallResult> DeleteTagAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string name)
    {
        return SendBoundProjectRequestAsync(
            "delete_tag",
            null,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Name = name;
            },
            "{}");
    }

    public Task<WorkerCallResult> CreateUserConstantAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string dataType,
        string value)
    {
        return SendBoundProjectRequestAsync(
            "create_user_constant",
            null,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Name = name;
                request.DataType = dataType;
                request.Value = value;
            },
            "{}");
    }

    public Task<WorkerCallResult> UpdateUserConstantAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string name,
        string? dataType,
        string? value)
    {
        return SendBoundProjectRequestAsync(
            "update_user_constant",
            null,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Name = name;
                request.DataType = dataType;
                request.Value = value;
            },
            "{}");
    }

    public Task<WorkerCallResult> DeleteUserConstantAsync(
        string? plcName,
        string tableName,
        string? folderPath,
        string name)
    {
        return SendBoundProjectRequestAsync(
            "delete_user_constant",
            null,
            request =>
            {
                request.PlcName = plcName;
                request.TableName = tableName;
                request.FolderPath = folderPath;
                request.Name = name;
            },
            "{}");
    }

    public Task<WorkerCallResult> CreateBlockAsync(
        string blockPath,
        string blockType,
        string? language,
        string? obEventClass)
    {
        return SendBoundProjectRequestAsync(
            "create_block",
            null,
            request =>
            {
                request.BlockPath = blockPath;
                request.BlockType = blockType;
                request.Language = language;
                request.OBEventClass = obEventClass;
            },
            "{}");
    }

    public Task<WorkerCallResult> DeleteBlockAsync(string blockPath)
    {
        return SendBoundProjectRequestAsync(
            "delete_block",
            null,
            request =>
            {
                request.BlockPath = blockPath;
            },
            "{}");
    }

    public Task<WorkerCallResult> CreateBlockGroupAsync(string blockPath)
    {
        return SendBoundProjectRequestAsync(
            "create_block_group",
            null,
            request =>
            {
                request.BlockPath = blockPath;
            },
            "{}");
    }

    public Task<WorkerCallResult> DeleteBlockGroupAsync(string blockPath)
    {
        return SendBoundProjectRequestAsync(
            "delete_block_group",
            null,
            request =>
            {
                request.BlockPath = blockPath;
            },
            "{}");
    }
}
