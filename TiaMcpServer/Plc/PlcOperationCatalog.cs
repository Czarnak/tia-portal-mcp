using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// Whitelists the dedicated PLC read and write operations and validates a batch before any worker
/// call. Pure and Siemens-free.
/// </summary>
public static class PlcOperationCatalog
{
    public const int MaxBatchSize = 50;
    public const int MaxOperationIdLength = 256;

    private sealed record Spec(IReadOnlyList<string> Required, IReadOnlyList<string> Optional, bool IsWrite);

    private static readonly IReadOnlyList<string> None = Array.Empty<string>();

    private static Spec Read(string[] required, string[] optional) => new(required, optional, false);

    private static Spec Write(string[] required, string[] optional) => new(required, optional, true);

    private static readonly IReadOnlyDictionary<string, Spec> Specs = new Dictionary<string, Spec>(StringComparer.Ordinal)
    {
        ["get_block_content"] = Read(new[] { "blockPath" }, new[] { "format", "withDependencies" }),
        ["get_type_content"] = Read(new[] { "typePath" }, new[] { "format", "withDependencies" }),
        ["list_tag_tables"] = Read(Array.Empty<string>(), new[] { "plcName", "tableName", "folderPath" }),

        // Write field sets are the batch catalog's, with yamlContent/sourceContent renamed to content and
        // the two content updates also requiring the hash of the document they were based on.
        ["update_block_logic"] = Write(new[] { "blockPath", "content", "expectedContentHash" }, new[] { "format" }),
        ["update_type_content"] = Write(new[] { "typePath", "content", "expectedContentHash" }, new[] { "format" }),
        ["create_tag_table"] = Write(new[] { "tableName" }, new[] { "plcName", "folderPath" }),
        ["delete_tag_table"] = Write(new[] { "tableName" }, new[] { "plcName", "folderPath" }),
        ["create_tag"] = Write(new[] { "tableName", "name", "dataType" }, new[] { "plcName", "folderPath", "logicalAddress" }),
        ["update_tag"] = Write(
            new[] { "tableName", "name" },
            new[] { "plcName", "folderPath", "newName", "dataType", "logicalAddress", "externalAccessible", "externalVisible", "externalWritable", "isSafety" }),
        ["delete_tag"] = Write(new[] { "tableName", "name" }, new[] { "plcName", "folderPath" }),
        ["create_user_constant"] = Write(new[] { "tableName", "name", "dataType", "value" }, new[] { "plcName", "folderPath" }),
        ["update_user_constant"] = Write(new[] { "tableName", "name" }, new[] { "plcName", "folderPath", "dataType", "value" }),
        ["delete_user_constant"] = Write(new[] { "tableName", "name" }, new[] { "plcName", "folderPath" }),
        ["create_block"] = Write(new[] { "blockPath", "blockType" }, new[] { "language", "obEventClass" }),
        ["delete_block"] = Write(new[] { "blockPath" }, Array.Empty<string>()),
        ["create_block_group"] = Write(new[] { "blockPath" }, Array.Empty<string>()),
        ["delete_block_group"] = Write(new[] { "blockPath" }, Array.Empty<string>()),
    };

    /// <summary>Wire name, getter and "is a nonblank value required when listed as required" for every string field.</summary>
    private static readonly (string Name, Func<PlcOperationRequest, string?> Get)[] StringFields =
    {
        ("blockPath", op => op.BlockPath),
        ("typePath", op => op.TypePath),
        ("format", op => op.Format),
        ("plcName", op => op.PlcName),
        ("tableName", op => op.TableName),
        ("folderPath", op => op.FolderPath),
        ("content", op => op.Content),
        ("expectedContentHash", op => op.ExpectedContentHash),
        ("name", op => op.Name),
        ("newName", op => op.NewName),
        ("dataType", op => op.DataType),
        ("logicalAddress", op => op.LogicalAddress),
        ("value", op => op.Value),
        ("blockType", op => op.BlockType),
        ("language", op => op.Language),
        ("obEventClass", op => op.ObEventClass),
    };

    private static readonly (string Name, Func<PlcOperationRequest, bool> IsSet)[] OperationFields =
        StringFields
            .Select(field => (field.Name, IsSet: new Func<PlcOperationRequest, bool>(op => field.Get(op) is not null)))
            .Concat(new (string, Func<PlcOperationRequest, bool>)[]
            {
                ("withDependencies", op => op.WithDependencies is not null),
                ("externalAccessible", op => op.ExternalAccessible is not null),
                ("externalVisible", op => op.ExternalVisible is not null),
                ("externalWritable", op => op.ExternalWritable is not null),
                ("isSafety", op => op.IsSafety is not null),
            })
            .ToArray();

    private static readonly IReadOnlySet<string> GroupPathOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "create_block",
        "create_block_group",
        "delete_block_group",
    };

    public static IReadOnlyList<string> ReadOperationNames { get; } =
        Specs.Where(entry => !entry.Value.IsWrite).Select(entry => entry.Key).ToArray();

    public static IReadOnlyList<string> WriteOperationNames { get; } =
        Specs.Where(entry => entry.Value.IsWrite).Select(entry => entry.Key).ToArray();

    /// <summary>The declared required and optional wire fields of a write operation.</summary>
    public static bool TryGetWriteFields(
        string operation,
        out IReadOnlyList<string> required,
        out IReadOnlyList<string> optional)
    {
        if (Specs.TryGetValue(operation, out var spec) && spec.IsWrite)
        {
            required = spec.Required;
            optional = spec.Optional;
            return true;
        }

        required = optional = None;
        return false;
    }

    public static (bool IsValid, string Error) ValidateRead(IReadOnlyList<PlcOperationRequest>? operations)
        => Validate(operations, write: false);

    public static (bool IsValid, string Error) ValidateWrite(IReadOnlyList<PlcOperationRequest>? operations)
        => Validate(operations, write: true);

    public static IReadOnlyList<string> ValidateAccessMode(
        IReadOnlyList<PlcOperationRequest> operations,
        McpAccessMode mode)
    {
        var errors = new List<string>();
        foreach (var operation in operations)
        {
            if (operation is null || string.IsNullOrWhiteSpace(operation.Operation))
            {
                continue;
            }

            if (!OperationPolicyCatalog.IsAllowed(mode, operation.Operation))
            {
                errors.Add(
                    $"Operation '{operation.Operation}' (operationId '{operation.OperationId}') is not permitted in {McpAccessModeNames.ToName(mode)} mode.");
            }
        }

        return errors;
    }

    private static (bool IsValid, string Error) Validate(IReadOnlyList<PlcOperationRequest>? operations, bool write)
    {
        if (operations is null || operations.Count == 0)
        {
            return (false, "Batch must contain at least one operation.");
        }

        if (operations.Count > MaxBatchSize)
        {
            return (false, $"Batch exceeds the maximum of {MaxBatchSize} operations (received {operations.Count}).");
        }

        var errors = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            ValidateItem(operation, seenIds, errors, write);
        }

        if (write)
        {
            ValidateWriteCall(operations, errors);
        }

        return errors.Count == 0 ? (true, string.Empty) : (false, string.Join("\n", errors));
    }

    private static void ValidateItem(PlcOperationRequest? operation, HashSet<string> seenIds, List<string> errors, bool write)
    {
        if (operation is null)
        {
            errors.Add("Batch contains a null operation.");
            return;
        }

        if (string.IsNullOrWhiteSpace(operation.OperationId))
        {
            errors.Add("Each operation requires a unique operationId.");
            return;
        }

        if (operation.OperationId.Length > MaxOperationIdLength)
        {
            errors.Add($"Each operationId must be at most {MaxOperationIdLength} characters.");
            return;
        }

        if (!seenIds.Add(operation.OperationId))
        {
            errors.Add($"Duplicate operationId '{operation.OperationId}'.");
            return;
        }

        if (string.IsNullOrWhiteSpace(operation.Operation))
        {
            errors.Add($"Operation name is required for operationId '{operation.OperationId}'.");
            return;
        }

        if (!Specs.TryGetValue(operation.Operation, out var spec))
        {
            errors.Add(
                $"Unknown operation '{operation.Operation}' for operationId '{operation.OperationId}'. "
                + $"Valid {(write ? "write" : "read")} operations: "
                + $"{string.Join(", ", write ? WriteOperationNames : ReadOperationNames)}.");
            return;
        }

        if (spec.IsWrite != write)
        {
            errors.Add(write
                ? $"'{operation.Operation}' is a read operation; use plc_read."
                : $"'{operation.Operation}' is a write operation; use plc_write.");
            return;
        }

        var prefix = $"Operation '{operation.Operation}' (operationId '{operation.OperationId}')";
        foreach (var (name, isSet) in OperationFields)
        {
            if (isSet(operation) && !spec.Required.Contains(name) && !spec.Optional.Contains(name))
            {
                var valid = spec.Optional.Count > 0 ? string.Join(", ", spec.Optional) : "(none)";
                errors.Add($"{prefix}: '{name}' is not valid for {operation.Operation}. Valid optional fields: {valid}.");
            }
        }

        var missing = spec.Required.Where(field => !IsPresent(operation, field)).ToArray();
        if (missing.Length > 0)
        {
            errors.Add($"{prefix} is missing required field(s): {string.Join(", ", missing)}.");
        }

        foreach (var (name, value) in new[]
                 {
                     ("plcName", operation.PlcName), ("tableName", operation.TableName), ("folderPath", operation.FolderPath),
                 })
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"{prefix}: '{name}' must be nonblank when supplied.");
            }
        }

        var formatIsValid = true;
        if (operation.Format is not null)
        {
            try
            {
                _ = PlcFormatNames.Normalize(operation.Operation, operation.Format);
            }
            catch (ArgumentException exception)
            {
                formatIsValid = false;
                errors.Add($"{prefix}: {exception.Message}");
            }
        }

        if (write)
        {
            ValidateWriteItem(operation, prefix, formatIsValid, errors);
        }
    }

    private static void ValidateWriteItem(PlcOperationRequest operation, string prefix, bool formatIsValid, List<string> errors)
    {
        if (GroupPathOperations.Contains(operation.Operation) && !string.IsNullOrWhiteSpace(operation.BlockPath))
        {
            ValidateGroupPath(operation.BlockPath, prefix, errors);
        }

        if (operation.Operation == "create_block")
        {
            ValidateObCreation(operation, prefix, errors);
        }

        if (!string.IsNullOrWhiteSpace(operation.ExpectedContentHash) && formatIsValid)
        {
            ValidateHash(operation, prefix, errors);
        }
    }

    private static void ValidateObCreation(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        var isOb = string.Equals(operation.BlockType, "OB", StringComparison.OrdinalIgnoreCase);
        if (operation.ObEventClass is not null)
        {
            if (!isOb)
            {
                errors.Add($"{prefix}: obEventClass applies only to blockType OB.");
            }
            else if (!ObEventClasses.TryGet(operation.ObEventClass, out _))
            {
                errors.Add($"{prefix}: obEventClass '{operation.ObEventClass}' is not valid. Valid values: {ObEventClasses.NamesForMessage()}.");
            }
        }

        if (isOb && string.Equals(operation.Language, "GRAPH", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{prefix}: language GRAPH is not supported for blockType OB.");
        }
    }

    private static void ValidateGroupPath(string blockPath, string prefix, List<string> errors)
    {
        try
        {
            if (BlockAddress.Parse(blockPath).PlcName is null)
            {
                errors.Add(
                    $"{prefix}: blockPath '{blockPath}' is a single name. Use 'PLC/Name' (the PLC root group) "
                    + "or the deterministic 'PLC/Blocks/.../Name'.");
            }
        }
        catch (ArgumentException exception)
        {
            errors.Add($"{prefix}: {exception.Message}");
        }
    }

    private static void ValidateHash(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        var hash = operation.ExpectedContentHash!;
        if (!ContentHashRules.TryParse(hash, out var hashFormat, out _))
        {
            errors.Add(
                $"{prefix}: expectedContentHash '{hash}' is malformed. Expected '<format>:sha256:<64 lower-case hex>' "
                + $"with format one of: {string.Join(", ", SourceFormatNames.Allowed)}.");
            return;
        }

        var writeFormat = PlcFormatNames.Normalize(operation.Operation, operation.Format);
        if (!string.Equals(hashFormat, writeFormat, StringComparison.Ordinal))
        {
            errors.Add(
                $"{prefix}: expectedContentHash was taken from the '{hashFormat}' format but the write uses the "
                + $"'{writeFormat}' format. Read the object in the write format and use that hash.");
        }
    }

    private static void ValidateWriteCall(IReadOnlyList<PlcOperationRequest> operations, List<string> errors)
    {
        var createdBlocks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (operation is null || string.IsNullOrWhiteSpace(operation.BlockPath))
            {
                continue;
            }

            if (operation.Operation == "create_block")
            {
                createdBlocks.Add(BlockKey(operation.BlockPath));
            }
            else if (operation.Operation == "update_block_logic" && createdBlocks.Contains(BlockKey(operation.BlockPath)))
            {
                errors.Add(
                    $"Operation 'update_block_logic' (operationId '{operation.OperationId}'): block '{operation.BlockPath}' "
                    + "is created earlier in this call, so no content hash can exist for it. Read it after the call.");
            }
        }

        var distinctPaths = operations
            .Where(operation => operation is not null && !string.IsNullOrWhiteSpace(operation.ProjectPath))
            .Select(operation => WriteProjectPaths.Normalize(operation.ProjectPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (distinctPaths > 1)
        {
            errors.Add("All write operations in a batch must target the same project path.");
        }
    }

    /// <summary>Spelling-independent identity of a block path ('PLC/Name' equals 'PLC/Blocks/Name').</summary>
    private static string BlockKey(string blockPath)
    {
        try
        {
            var address = BlockAddress.Parse(blockPath);
            return string.Join(
                "/",
                new[] { address.PlcName ?? string.Empty, address.UnitName ?? string.Empty }
                    .Concat(address.FolderPath)
                    .Append(address.BlockName)).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return blockPath.Trim().ToLowerInvariant();
        }
    }

    private static bool IsPresent(PlcOperationRequest operation, string field)
        => StringFields.Any(entry => entry.Name == field && !string.IsNullOrWhiteSpace(entry.Get(operation)));
}
