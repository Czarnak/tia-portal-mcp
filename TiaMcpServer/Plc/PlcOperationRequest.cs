using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Plc;

/// <summary>Strict request shape for one dedicated PLC operation; the catalog restricts fields per operation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcOperationRequest : IOperationBatchItem
{
    [Description("Client-supplied unique identifier for this PLC operation; returned results are keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("PLC operation to run. Reads (plc_read): get_block_content, get_type_content, list_tag_tables. Writes (plc_write): update_block_logic, update_type_content, create_tag_table, delete_tag_table, create_tag, update_tag, delete_tag, create_user_constant, update_user_constant, delete_user_constant, create_block, delete_block, create_block_group, delete_block_group.")]
    public string Operation { get; set; } = string.Empty;

    [Description("Optional absolute project path (.ap21). Reads never open or switch a project; a path that differs from the bound project fails only this item with binding_conflict.")]
    public string? ProjectPath { get; set; }

    [Description("PLC block path, e.g. PLC_1/Main or PLC_1/Blocks/Folder/Block. Required by get_block_content, update_block_logic, create_block, delete_block, create_block_group and delete_block_group. create_block, create_block_group and delete_block_group also accept PLC/Name (the PLC root group).")]
    public string? BlockPath { get; set; }

    [Description("PLC data type path, e.g. PLC_1/Types/AnalogInputSettings. Required by get_type_content and update_type_content.")]
    public string? TypePath { get; set; }

    [Description("Document format. Valid values: source, xml. update_block_logic and update_type_content take the write format and expectedContentHash must carry the same format. get_type_content and update_type_content default to source; get_block_content defaults to xml and honors source for global data blocks and SCL-language FB/FC/OB.")]
    public string? Format { get; set; }

    [Description("Include the object's dependency closure in the exported source. Optional for get_block_content and get_type_content; only meaningful when format is source. The result is context only and carries no contentHash.")]
    public bool? WithDependencies { get; set; }

    [Description("Optional PLC software name. For list_tag_tables, when omitted every PLC is read. For plc_write tag, tag-table and user-constant operations it scopes the target; an omitted name that matches several PLCs fails with target_ambiguous.")]
    public string? PlcName { get; set; }

    [Description("Tag table name (case-insensitive). Optional for list_tag_tables (returns only that table; no match fails the item with target_not_found); required by the tag, tag-table and user-constant write operations.")]
    public string? TableName { get; set; }

    [Description("Optional tag table folder for list_tag_tables and the plc_write tag, tag-table and user-constant operations, as the inventory emits it in folderPath (\"/\" for the root, e.g. /Line/Cell; case-insensitive). Returns only tables directly in that folder; no match fails the item with target_not_found.")]
    public string? FolderPath { get; set; }

    [Description("Full replacement document for update_block_logic (Simatic ML XML or external source) or update_type_content (external source or Simatic ML XML), in the write format. Required by both.")]
    public string? Content { get; set; }

    [Description("Format-tagged contentHash from a plc_read of the same object in the same format (xml:sha256:<hex> or source:sha256:<hex>). Required by update_block_logic and update_type_content; a changed object fails with state_changed.")]
    public string? ExpectedContentHash { get; set; }

    [Description("Tag or user-constant name. Required by create/update/delete tag and user-constant operations.")]
    public string? Name { get; set; }

    [Description("Optional new name when renaming a tag; only update_tag applies it. Renaming user constants is not supported.")]
    public string? NewName { get; set; }

    [Description("Data type, e.g. Bool or Int. Required by create_tag and create_user_constant.")]
    public string? DataType { get; set; }

    [Description("Optional logical address, e.g. %I0.0.")]
    public string? LogicalAddress { get; set; }

    [Description("Constant value. Required by create_user_constant; optional for update_user_constant.")]
    public string? Value { get; set; }

    [Description("Optional tag attribute: external accessibility.")]
    public bool? ExternalAccessible { get; set; }

    [Description("Optional tag attribute: external visibility.")]
    public bool? ExternalVisible { get; set; }

    [Description("Optional tag attribute: external writability.")]
    public bool? ExternalWritable { get; set; }

    [Description("Optional flag marking the tag as a safety tag.")]
    public bool? IsSafety { get; set; }

    [Description("Block type for create_block. Valid values: FB, FC, OB, GlobalDB.")]
    public string? BlockType { get; set; }

    [Description("Programming language for create_block (FB/FC/OB only). Valid values: LAD, FBD, STL, SCL, GRAPH. Defaults to LAD. Omit for blockType=GlobalDB, which always uses DB.")]
    public string? Language { get; set; }

    [Description("OB event class for create_block when blockType=OB; defaults to ProgramCycle. The server assigns the OB number.")]
    public string? ObEventClass { get; set; }
}
