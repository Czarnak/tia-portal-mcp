using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

/// <summary>Exact current state that an <c>update_tag</c> safety token must bind to.</summary>
public sealed record TagUpdateSafetySnapshot(
    [property: JsonPropertyName("plcName")] string PlcName,
    [property: JsonPropertyName("folderPath")] string FolderPath,
    [property: JsonPropertyName("tableName")] string TableName,
    [property: JsonPropertyName("tagName")] string TagName,
    [property: JsonPropertyName("dataType")] string DataType,
    [property: JsonPropertyName("logicalAddress")] string LogicalAddress,
    [property: JsonPropertyName("externalAccessible")] bool? ExternalAccessible,
    [property: JsonPropertyName("externalVisible")] bool? ExternalVisible,
    [property: JsonPropertyName("externalWritable")] bool? ExternalWritable);
