using TiaMcpServer.Batch;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public class BatchOperationCatalogTests
{
    private static BatchOperationRequest Op(string id, string operation, Action<BatchOperationRequest>? configure = null)
    {
        var request = new BatchOperationRequest { OperationId = id, Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    [Fact]
    public void ValidateWriteBatch_AcceptsKnownDataWrites()
    {
        var operations = new List<BatchOperationRequest>
        {
            Op("a", "create_tag", r => { r.TableName = "Inputs"; r.Name = "Start"; r.DataType = "Bool"; }),
            Op("b", "update_block_logic", r => { r.BlockPath = "Main"; r.YamlContent = "name: Main"; }),
            Op("c", "create_block_group", r => r.BlockPath = "PLC_1/Blocks/Area"),
        };

        var result = BatchOperationCatalog.ValidateWriteBatch(operations);

        Assert.True(result.IsValid, result.Error);
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
    public void AllWriteOperations_AcceptARequestWithTheirRequiredFields()
    {
        foreach (var operation in BatchOperationCatalog.WriteOperationNames)
        {
            var result = BatchOperationCatalog.ValidateWriteBatch(new[] { FullyPopulated("id", operation) });
            Assert.True(result.IsValid, $"{operation}: {result.Error}");
        }
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
    public void All_ExposesEverySpec()
    {
        // 16 writes.
        Assert.Equal(16, BatchOperationCatalog.All.Count);
    }

    [Fact]
    public void All_MatchesTheAuthoritativeOperationFieldContract()
    {
        var none = Array.Empty<string>();
        var expected = new Dictionary<string, (IReadOnlyList<string> Required, IReadOnlyList<string> Optional)>
        {
            ["update_block_logic"] = (new[] { "blockPath", "yamlContent" }, new[] { "format" }),
            ["create_tag_table"] = (new[] { "tableName" }, new[] { "plcName", "folderPath" }),
            ["delete_tag_table"] = (new[] { "tableName" }, new[] { "plcName", "folderPath" }),
            ["create_tag"] = (new[] { "tableName", "name", "dataType" }, new[] { "plcName", "folderPath", "logicalAddress" }),
            ["update_tag"] = (new[] { "tableName", "name" }, new[] { "plcName", "folderPath", "newName", "dataType", "logicalAddress", "externalAccessible", "externalVisible", "externalWritable", "isSafety" }),
            ["delete_tag"] = (new[] { "tableName", "name" }, new[] { "plcName", "folderPath" }),
            ["create_user_constant"] = (new[] { "tableName", "name", "dataType", "value" }, new[] { "plcName", "folderPath" }),
            ["update_user_constant"] = (new[] { "tableName", "name" }, new[] { "plcName", "folderPath", "dataType", "value" }),
            ["delete_user_constant"] = (new[] { "tableName", "name" }, new[] { "plcName", "folderPath" }),
            ["create_block"] = (new[] { "blockPath", "blockType" }, new[] { "language", "obEventClass" }),
            ["delete_block"] = (new[] { "blockPath" }, none),
            ["create_block_group"] = (new[] { "blockPath" }, none),
            ["delete_block_group"] = (new[] { "blockPath" }, none),
            ["start_plc"] = (none, new[] { "plcName" }),
            ["stop_plc"] = (none, new[] { "plcName" }),
            ["update_type_content"] = (new[] { "typePath", "sourceContent" }, new[] { "format" }),
        };
        var actual = BatchOperationCatalog.All.ToDictionary(spec => spec.Name, StringComparer.Ordinal);

        Assert.Equal(expected.Keys.OrderBy(name => name), actual.Keys.OrderBy(name => name));
        foreach (var (name, expectedSpec) in expected)
        {
            var actualSpec = actual[name];
            Assert.Equal(expectedSpec.Required, actualSpec.RequiredFields);
            Assert.Equal(expectedSpec.Optional, actualSpec.OptionalFields);
        }
    }

    [Fact]
    public void CreateTag_DoesNotDeclareTheExternalAttributes()
    {
        Assert.True(BatchOperationCatalog.TryGetSpec("create_tag", out var spec));

        Assert.DoesNotContain("externalAccessible", spec!.OptionalFields);
        Assert.DoesNotContain("externalVisible", spec.OptionalFields);
        Assert.DoesNotContain("externalWritable", spec.OptionalFields);
        Assert.DoesNotContain("isSafety", spec.OptionalFields);
    }

    [Fact]
    public void UpdateTag_DeclaresTheExternalAttributes()
    {
        Assert.True(BatchOperationCatalog.TryGetSpec("update_tag", out var spec));

        Assert.Contains("externalAccessible", spec!.OptionalFields);
        Assert.Contains("externalVisible", spec.OptionalFields);
        Assert.Contains("externalWritable", spec.OptionalFields);
        Assert.Contains("isSafety", spec.OptionalFields);
        Assert.Contains("newName", spec.OptionalFields);
    }

    [Fact]
    public void NoSpecDeclaresAUniversalFieldAsOptional()
    {
        foreach (var spec in BatchOperationCatalog.All)
        {
            Assert.DoesNotContain("operationId", spec.OptionalFields);
            Assert.DoesNotContain("operation", spec.OptionalFields);
            Assert.DoesNotContain("projectPath", spec.OptionalFields);
        }
    }

    [Fact]
    public void RequiredAndOptionalFieldsNeverOverlap()
    {
        foreach (var spec in BatchOperationCatalog.All)
        {
            Assert.Empty(spec.RequiredFields.Intersect(spec.OptionalFields));
        }
    }

    [Fact]
    public void ExternalAttributesOnCreateTag_AreRejected()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[]
        {
            new BatchOperationRequest
            {
                OperationId = "a",
                Operation = "create_tag",
                TableName = "Default tag table",
                Name = "Motor",
                DataType = "Bool",
                ExternalAccessible = true,
                IsSafety = false
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains("externalAccessible", result.Error);
        Assert.Contains("isSafety", result.Error);
    }

    [Fact]
    public void ExternalAttributesOnUpdateTag_AreAccepted()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[]
        {
            new BatchOperationRequest
            {
                OperationId = "a",
                Operation = "update_tag",
                TableName = "Default tag table",
                Name = "Motor",
                ExternalAccessible = true,
                IsSafety = false
            }
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void InapplicableFieldErrors_AggregateWithOtherErrors()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[]
        {
            new BatchOperationRequest
            {
                OperationId = "a",
                Operation = "create_tag",
                TableName = "Default tag table",
                ExternalAccessible = true
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains("missing required field(s)", result.Error);
        Assert.Contains("externalAccessible", result.Error);
    }

    [Fact]
    public void ObEventClassOnCreateBlock_IsAccepted()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[]
        {
            new BatchOperationRequest
            {
                OperationId = "a",
                Operation = "create_block",
                BlockPath = "PLC_1/Blocks/Main",
                BlockType = "OB",
                ObEventClass = "ProgramCycle"
            }
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void NetworkOperations_AreExposedOnlyByTheDedicatedCatalog()
    {
        Assert.Equal(
            new[] { "read_hardware_config", "search_equipment_catalog", "list_network_objects", "inspect_network_object" },
            NetworkOperationCatalog.ReadOperationNames);
        Assert.Equal(
            new[] { "add_network_device", "configure_network_device", "create_subnet", "update_subnet", "delete_subnet" },
            NetworkOperationCatalog.WriteOperationNames);

        foreach (var operation in new[]
        {
            "read_hardware_config",
            "search_equipment_catalog",
            "list_network_objects",
            "inspect_network_object",
            "add_network_device",
            "configure_network_device",
            "create_subnet",
            "update_subnet",
            "delete_subnet"
        })
        {
            Assert.DoesNotContain(operation, BatchOperationCatalog.WriteOperationNames);
        }
    }

    [Theory]
    [InlineData("get_block_content")]
    [InlineData("get_type_content")]
    [InlineData("list_tag_tables")]
    [InlineData("read_cross_references")]
    public void ReadOperationInWriteBatchNamesNewTools(string operation)
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { Op("a", operation) });

        Assert.False(result.IsValid);
        Assert.Equal($"'{operation}' is a read operation; use plc_read or read_cross_references.", result.Error);
    }

    [Fact]
    public void RetiredReadBatchSurface_IsGone()
    {
        Assert.Null(typeof(BatchOperationCatalog).GetMethod("ValidateReadBatch"));
        Assert.Null(typeof(BatchOperationCatalog).GetProperty("ReadOperationNames"));
        Assert.Null(typeof(BatchOperationRequest).GetProperty("Filter"));
        Assert.Null(typeof(BatchOperationRequest).GetProperty("MaxResults"));
        Assert.Null(typeof(BatchOperationRequest).GetProperty("WithDependencies"));
    }

    [Theory]
    [InlineData("get_project_status")]
    [InlineData("browse_project_tree")]
    [InlineData("compile_check")]
    public void ValidateWriteBatch_RejectsStandaloneProjectOperations(string operation)
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { Op("a", operation) });

        Assert.False(result.IsValid);
        Assert.Contains($"Unknown operation '{operation}'", result.Error);
    }

    [Fact]
    public void UniversalFields_AreNeverRejected()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[]
        {
            new BatchOperationRequest
            {
                OperationId = "a",
                Operation = "start_plc",
                ProjectPath = @"C:\p.ap21"
            }
        });

        Assert.True(result.IsValid, result.Error);
    }

    private static BatchOperationRequest FullyPopulated(string id, string operation)
    {
        Assert.True(BatchOperationCatalog.TryGetSpec(operation, out var spec));
        var request = new BatchOperationRequest { OperationId = id, Operation = operation };

        foreach (var field in spec!.RequiredFields)
        {
            switch (field)
            {
                case "blockPath": request.BlockPath = "PLC_1/Main"; break;
                case "yamlContent": request.YamlContent = "name: Main"; break;
                case "blockType": request.BlockType = "FB"; break;
                case "tableName": request.TableName = "Inputs"; break;
                case "name": request.Name = "Item"; break;
                case "dataType": request.DataType = "Bool"; break;
                case "value": request.Value = "1"; break;
                case "typePath": request.TypePath = "PLC_1/Types/AnalogInputSettings"; break;
                case "sourceContent": request.SourceContent = "TYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n"; break;
                default: throw new InvalidOperationException($"No test value configured for required field '{field}'.");
            }
        }

        return request;
    }
}
