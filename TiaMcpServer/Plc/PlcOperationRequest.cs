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

    [Description("PLC operation to run: get_block_content, get_type_content, or list_tag_tables.")]
    public string Operation { get; set; } = string.Empty;

    [Description("Optional absolute project path (.ap21). Reads never open or switch a project; a path that differs from the bound project fails only this item with binding_conflict.")]
    public string? ProjectPath { get; set; }

    [Description("PLC block path, e.g. PLC_1/Main or PLC_1/Blocks/Folder/Block. Required by get_block_content.")]
    public string? BlockPath { get; set; }

    [Description("PLC data type path, e.g. PLC_1/Types/AnalogInputSettings. Required by get_type_content.")]
    public string? TypePath { get; set; }

    [Description("Document format. Valid values: source, xml. get_type_content defaults to source; get_block_content defaults to xml and honors source for global data blocks and SCL-language FB/FC/OB.")]
    public string? Format { get; set; }

    [Description("Include the object's dependency closure in the exported source. Optional for get_block_content and get_type_content; only meaningful when format is source. The result is context only and carries no contentHash.")]
    public bool? WithDependencies { get; set; }

    [Description("Optional PLC software name for list_tag_tables. When omitted, every PLC in the project is read.")]
    public string? PlcName { get; set; }
}
