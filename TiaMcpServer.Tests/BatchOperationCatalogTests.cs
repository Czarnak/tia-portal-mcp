using System.Collections.Generic;
using System.Linq;
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
            Op("a", "read_hardware_config"),
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
            operations.Add(Op($"id{i}", "read_hardware_config"));
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
            new[] { Op("dup", "read_hardware_config"), Op("dup", "list_tag_tables") });

        Assert.False(result.IsValid);
        Assert.Contains("dup", result.Error);
    }

    [Fact]
    public void ValidateReadBatch_RejectsMissingOperationId()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[] { Op("", "read_hardware_config") });

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
    public void AllWriteOperations_AcceptARequestWithTheirRequiredFields()
    {
        foreach (var operation in BatchOperationCatalog.WriteOperationNames)
        {
            var result = BatchOperationCatalog.ValidateWriteBatch(new[] { FullyPopulated("id", operation) });
            Assert.True(result.IsValid, $"{operation}: {result.Error}");
        }
    }

    [Fact]
    public void AllReadOperations_AcceptARequestWithTheirRequiredFields()
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
        Assert.Contains("get_type_content", result.Error);
        Assert.DoesNotContain("get_project_status", result.Error);
        Assert.DoesNotContain("browse_project_tree", result.Error);
        Assert.DoesNotContain("compile_check", result.Error);
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
    public void Validate_RejectsMaxResultsOnUnsupportedOperations()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "list_tag_tables", MaxResults = 10 },
        };

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.False(result.IsValid);
        Assert.Contains("'maxResults' is not valid for list_tag_tables", result.Error);
    }

    [Fact]
    public void Validate_RejectsOutOfRangeBounds()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "search_equipment_catalog", Query = "cpu", MaxResults = 0 },
        };

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.False(result.IsValid);
        Assert.Contains("'maxResults' must be 1 or greater", result.Error);
    }

    [Fact]
    public void Validate_AcceptsBoundsOnTheirOperations()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "search_equipment_catalog", Query = "cpu", MaxResults = 10 },
            new BatchOperationRequest { OperationId = "b", Operation = "read_cross_references", MaxResults = 100 },
        };

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void All_ExposesEverySpec()
    {
        // 6 reads + 18 writes.
        Assert.Equal(24, BatchOperationCatalog.All.Count);
    }

    [Fact]
    public void All_MatchesTheAuthoritativeOperationFieldContract()
    {
        var none = Array.Empty<string>();
        var expected = new Dictionary<string, (BatchOperationCategory Category, IReadOnlyList<string> Required, IReadOnlyList<string> Optional)>
        {
            ["read_hardware_config"] = (BatchOperationCategory.Read, none, none),
            ["search_equipment_catalog"] = (BatchOperationCategory.Read, new[] { "query" }, new[] { "maxResults" }),
            ["read_cross_references"] = (BatchOperationCategory.Read, none, new[] { "plcName", "filter", "maxResults" }),
            ["get_block_content"] = (BatchOperationCategory.Read, new[] { "blockPath" }, new[] { "format" }),
            ["list_tag_tables"] = (BatchOperationCategory.Read, none, new[] { "plcName" }),
            ["get_type_content"] = (BatchOperationCategory.Read, new[] { "typePath" }, new[] { "format" }),
            ["update_block_logic"] = (BatchOperationCategory.Write, new[] { "blockPath", "yamlContent" }, new[] { "format" }),
            ["create_tag_table"] = (BatchOperationCategory.Write, new[] { "tableName" }, new[] { "plcName", "folderPath" }),
            ["delete_tag_table"] = (BatchOperationCategory.Write, new[] { "tableName" }, new[] { "plcName", "folderPath" }),
            ["create_tag"] = (BatchOperationCategory.Write, new[] { "tableName", "name", "dataType" }, new[] { "plcName", "folderPath", "logicalAddress" }),
            ["update_tag"] = (BatchOperationCategory.Write, new[] { "tableName", "name" }, new[] { "plcName", "folderPath", "newName", "dataType", "logicalAddress", "externalAccessible", "externalVisible", "externalWritable", "isSafety" }),
            ["delete_tag"] = (BatchOperationCategory.Write, new[] { "tableName", "name" }, new[] { "plcName", "folderPath" }),
            ["create_user_constant"] = (BatchOperationCategory.Write, new[] { "tableName", "name", "dataType", "value" }, new[] { "plcName", "folderPath" }),
            ["update_user_constant"] = (BatchOperationCategory.Write, new[] { "tableName", "name" }, new[] { "plcName", "folderPath", "dataType", "value" }),
            ["delete_user_constant"] = (BatchOperationCategory.Write, new[] { "tableName", "name" }, new[] { "plcName", "folderPath" }),
            ["add_network_device"] = (BatchOperationCategory.Write, new[] { "typeIdentifier", "deviceName" }, new[] { "deviceItemName" }),
            ["configure_network_device"] = (BatchOperationCategory.Write, new[] { "deviceName" }, new[] { "ipAddress", "subnetMask", "pnDeviceName", "subnetName", "ioSystemName" }),
            ["create_block"] = (BatchOperationCategory.Write, new[] { "blockPath", "blockType" }, new[] { "language", "obEventClass" }),
            ["delete_block"] = (BatchOperationCategory.Write, new[] { "blockPath" }, none),
            ["create_block_group"] = (BatchOperationCategory.Write, new[] { "blockPath" }, none),
            ["delete_block_group"] = (BatchOperationCategory.Write, new[] { "blockPath" }, none),
            ["start_plc"] = (BatchOperationCategory.Write, none, new[] { "plcName" }),
            ["stop_plc"] = (BatchOperationCategory.Write, none, new[] { "plcName" }),
            ["update_type_content"] = (BatchOperationCategory.Write, new[] { "typePath", "sourceContent" }, new[] { "format" }),
        };
        var actual = BatchOperationCatalog.All.ToDictionary(spec => spec.Name, StringComparer.Ordinal);

        Assert.Equal(expected.Keys.OrderBy(name => name), actual.Keys.OrderBy(name => name));
        foreach (var (name, expectedSpec) in expected)
        {
            var actualSpec = actual[name];
            Assert.Equal(expectedSpec.Category, actualSpec.Category);
            Assert.Equal(expectedSpec.Required, actualSpec.RequiredFields);
            Assert.Equal(expectedSpec.Optional, actualSpec.OptionalFields);
        }
    }

    [Fact]
    public void ConfigureNetworkDevice_DoesNotDeclareDeviceItemName()
    {
        Assert.True(BatchOperationCatalog.TryGetSpec("configure_network_device", out var spec));

        Assert.DoesNotContain("deviceItemName", spec!.OptionalFields);
        Assert.Contains("ipAddress", spec.OptionalFields);
        Assert.Contains("subnetMask", spec.OptionalFields);
        Assert.Contains("pnDeviceName", spec.OptionalFields);
        Assert.Contains("subnetName", spec.OptionalFields);
        Assert.Contains("ioSystemName", spec.OptionalFields);
    }

    [Fact]
    public void AddNetworkDevice_DeclaresDeviceItemName()
    {
        Assert.True(BatchOperationCatalog.TryGetSpec("add_network_device", out var spec));

        Assert.Contains("deviceItemName", spec!.OptionalFields);
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
    public void DeviceItemNameOnConfigureNetworkDevice_IsRejected()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[]
        {
            new BatchOperationRequest
            {
                OperationId = "a",
                Operation = "configure_network_device",
                DeviceName = "PLC_1",
                DeviceItemName = "PROFINET interface_1"
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains("deviceItemName", result.Error);
        Assert.Contains("configure_network_device", result.Error);
        Assert.Contains("ipAddress", result.Error);
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
    public void UniversalFields_AreNeverRejected()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[]
        {
            new BatchOperationRequest
            {
                OperationId = "a",
                Operation = "read_hardware_config",
                ProjectPath = "C:\\p.ap21"
            }
        });

        Assert.True(result.IsValid);
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

    [Theory]
    [InlineData("get_project_status")]
    [InlineData("browse_project_tree")]
    [InlineData("compile_check")]
    public void ValidateReadBatch_RejectsStandaloneProjectOperations(string operation)
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[] { Op("a", operation) });

        Assert.False(result.IsValid);
        Assert.Contains($"Unknown operation '{operation}'", result.Error);
        Assert.DoesNotContain(operation, BatchOperationCatalog.ReadOperationNames);
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
                case "query": request.Query = "CPU"; break;
                case "tableName": request.TableName = "Inputs"; break;
                case "name": request.Name = "Item"; break;
                case "dataType": request.DataType = "Bool"; break;
                case "value": request.Value = "1"; break;
                case "typeIdentifier": request.TypeIdentifier = "OrderNumber:X/V1.0"; break;
                case "deviceName": request.DeviceName = "PLC_1"; break;
                case "typePath": request.TypePath = "PLC_1/Types/AnalogInputSettings"; break;
                case "sourceContent": request.SourceContent = "TYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n"; break;
                default: throw new InvalidOperationException($"No test value configured for required field '{field}'.");
            }
        }

        return request;
    }
}
