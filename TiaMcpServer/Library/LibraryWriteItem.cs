using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Library;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LibraryWriteItem : IOperationBatchItem
{
    [Description("Unique client-supplied operation identifier, at most 256 characters.")]
    public string OperationId { get; set; } = string.Empty;
    [Description("create_global, open_global, retrieve_global, save_global, close_global, archive_global, create_folder, delete_folder, create_master_copy, copy_master_copy, instantiate_master_copy, delete_master_copy, create_type, create_type_version, release_type_version, set_default_type_version, synchronize_type, instantiate_type_version, delete_type or delete_type_version.")]
    public string Operation { get; set; } = string.Empty;
    [Description("project or global; lifecycle selectors require global. For copy_master_copy this selects the destination.")]
    public string? Scope { get; set; }
    [Description("Absolute path of an already-open global library, case-insensitive. Supply this or libraryName; both must match when supplied together.")]
    public string? LibraryPath { get; set; }
    [Description("Exact name of an already-open global library. Project scope forbids libraryName and libraryPath.")]
    public string? LibraryName { get; set; }
    [Description("create_folder/delete_folder only: types or masterCopies.")]
    public string? FolderKind { get; set; }
    [Description("Exact library folder name segments; omit for root. For copy_master_copy this is the destination folder.")]
    public List<string>? FolderPath { get; set; }
    [Description("Folder/master-copy name, or output name for create_global/archive_global. Not accepted by create_master_copy, which uses the resolved PLC source name.")]
    public string? Name { get; set; }
    [Description("copy_master_copy, synchronize_type or instantiate_type_version source: project or global.")]
    public string? SourceScope { get; set; }
    public string? SourceLibraryPath { get; set; }
    public string? SourceLibraryName { get; set; }
    [Description("copy_master_copy source folder name segments; omit for root.")]
    public List<string>? SourceFolderPath { get; set; }
    [Description("Absolute output directory for create_global, retrieve_global or archive_global.")]
    public string? Directory { get; set; }
    [Description("Absolute input .al21 path for open_global, or .zal21 path for retrieve_global.")]
    public string? FilePath { get; set; }
    [Description("open_global/retrieve_global: readOnly or readWrite.")]
    public string? OpenMode { get; set; }
    [Description("close_global: save or discard. Discard always reports possible loss of changes.")]
    public string? CloseAction { get; set; }
    [Description("PLC software or owning-device selector for master-copy and type create/instantiate operations.")]
    public string? PlcName { get; set; }
    [Description("create_master_copy source: block, plcType or tagTable. create_type/create_type_version source: block or plcType. instantiate_master_copy destination: blockGroup, typeGroup or tagTableGroup.")]
    public string? PlcKind { get; set; }
    [Description("Case-insensitive PLC folder name segments; omit for root.")]
    public List<string>? PlcFolderPath { get; set; }
    [Description("create_master_copy, create_type or create_type_version: case-insensitive source object name.")]
    public string? PlcObjectName { get; set; }
    [Description("Exact type name in folderPath. Supply this or typeGuid for operations selecting an existing type.")]
    public string? TypeName { get; set; }
    [Description("Nonempty type GUID. Supply this or typeName.")]
    public string? TypeGuid { get; set; }
    [Description("Three numeric version parts, excluding 0.0.0. Release may omit this; set_default_type_version requires this or versionGuid; other version selectors accept this or versionGuid or inWork:true.")]
    public string? VersionNumber { get; set; }
    [Description("Nonempty version GUID, mutually exclusive with versionNumber and inWork.")]
    public string? VersionGuid { get; set; }
    [Description("Select the in-work version; only true is valid. Mutually exclusive with versionNumber and versionGuid.")]
    public bool? InWork { get; set; }
    [Description("release_type_version only: optional author.")]
    public string? Author { get; set; }
    [Description("release_type_version only: optional comment.")]
    public string? Comment { get; set; }
    [Description("create_type_version only: SimaticML XML content.")]
    public string? Content { get; set; }
    [Description("create_type_version only: required xml:sha256 content hash returned by plc_read.")]
    public string? ExpectedContentHash { get; set; }
    [Description("create_type_version only: permit replacing an existing in-work version.")]
    public bool? ReplaceInWork { get; set; }
}
