namespace TiaMcpServer.Contracts;

/// <summary>
/// Flat request envelope for one host→worker call, serialized as newline-delimited JSON.
///
/// The shape is deliberately flat rather than one DTO per operation: the protocol is stable
/// and per-operation types would cost more churn than they save. See "Deferred / explicitly
/// not planned" in docs/IMPROVEMENT_PLAN.md.
///
/// <para>
/// Only the fields relevant to <see cref="Method"/> are read; everything else is ignored.
/// Regions below group fields by the operation family that reads them, and each field
/// documents the exact operations that forward it. That list is the contract — a field not
/// named for an operation is silently dropped for that operation.
/// </para>
/// </summary>
public class WorkerRequest
{
    #region Common — dispatch and write-confirmation flags

    /// <summary>Operation name, dispatched by the worker's switch in Program.cs.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Target project path. Resolved against the session binding before sending.</summary>
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Set by every write operation EXCEPT update_block_logic, which forwards only
    /// AllowTiaConfirmations. Never set by reads.
    /// </summary>
    public bool Confirm { get; set; }

    /// <summary>Set by every write operation, including update_block_logic. Never set by reads.</summary>
    public bool AllowTiaConfirmations { get; set; }

    #endregion

    #region Block operations

    /// <summary>
    /// Forwarded by: get_block_content, update_block_logic, compile_check (optional, scopes
    /// the compile to one block), create_block, delete_block, create_block_group,
    /// delete_block_group.
    /// </summary>
    public string? BlockPath { get; set; }

    /// <summary>Forwarded by: update_block_logic.</summary>
    public string? YamlContent { get; set; }

    /// <summary>Forwarded by: create_block. Valid values: FB, FC, OB, GlobalDB.</summary>
    public string? BlockType { get; set; }

    /// <summary>
    /// Forwarded by: create_block. Passed through as-is including null — the worker applies
    /// the LAD default, not the host.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Forwarded by: create_block. Passed through as-is including null — the worker applies
    /// the ProgramCycle default, not the host.
    /// </summary>
    public string? OBEventClass { get; set; }

    #endregion

    #region PLC scoping, tag tables, tags, and user constants

    /// <summary>
    /// Forwarded by: read_cross_references, compile_check, list_tag_tables, start_plc,
    /// stop_plc, and every tag-table, tag, and user-constant operation.
    /// </summary>
    public string? PlcName { get; set; }

    /// <summary>Forwarded by: every tag-table, tag, and user-constant operation.</summary>
    public string? TableName { get; set; }

    /// <summary>Forwarded by: every tag-table, tag, and user-constant operation.</summary>
    public string? FolderPath { get; set; }

    /// <summary>
    /// Forwarded by: create_tag, update_tag, delete_tag, create_user_constant,
    /// update_user_constant, delete_user_constant.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Forwarded by: update_tag ONLY. Not forwarded by update_user_constant, which has no
    /// rename path despite exposing a similar shape.
    /// </summary>
    public string? NewName { get; set; }

    /// <summary>
    /// Forwarded by: create_tag, update_tag, create_user_constant, update_user_constant.
    /// </summary>
    public string? DataType { get; set; }

    /// <summary>Forwarded by: create_tag, update_tag.</summary>
    public string? LogicalAddress { get; set; }

    /// <summary>Forwarded by: create_user_constant, update_user_constant.</summary>
    public string? Value { get; set; }

    /// <summary>Forwarded by: update_tag ONLY. create_tag does not forward it.</summary>
    public bool? ExternalAccessible { get; set; }

    /// <summary>Forwarded by: update_tag ONLY. create_tag does not forward it.</summary>
    public bool? ExternalVisible { get; set; }

    /// <summary>Forwarded by: update_tag ONLY. create_tag does not forward it.</summary>
    public bool? ExternalWritable { get; set; }

    /// <summary>Forwarded by: update_tag ONLY. create_tag does not forward it.</summary>
    public bool? IsSafety { get; set; }

    #endregion

    #region Project tree, catalog, and cross-references

    /// <summary>Forwarded by: browse_project_tree.</summary>
    public int? Depth { get; set; }

    /// <summary>Forwarded by: browse_project_tree.</summary>
    public string? StartPath { get; set; }

    /// <summary>Forwarded by: search_equipment_catalog.</summary>
    public string? Query { get; set; }

    /// <summary>Forwarded by: search_equipment_catalog, read_cross_references.</summary>
    public int? MaxResults { get; set; }

    /// <summary>
    /// Forwarded by: read_cross_references. Populated from the batch item's `filter` field —
    /// the names differ — after CrossReferenceFilterNames.TryNormalize validates it. That
    /// validation runs BEFORE the session binds so an invalid filter cannot bind the session.
    /// </summary>
    public string? CrossReferenceFilter { get; set; }

    #endregion

    #region Network devices

    /// <summary>Forwarded by: add_network_device.</summary>
    public string? TypeIdentifier { get; set; }

    /// <summary>Forwarded by: add_network_device, configure_network_device.</summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// Forwarded by: add_network_device ONLY. configure_network_device does not forward it —
    /// setting it on that operation is silently dropped. The fallback to DeviceName when the
    /// caller omits it is applied by BatchWorkerInvoker.ResolveDeviceItemName before the call.
    /// </summary>
    public string? DeviceItemName { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? SubnetMask { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? PnDeviceName { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? SubnetName { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? IoSystemName { get; set; }

    #endregion

    #region Project lifecycle

    /// <summary>Forwarded by: create_project.</summary>
    public string? ProjectDirectory { get; set; }

    /// <summary>Forwarded by: create_project.</summary>
    public string? ProjectName { get; set; }

    /// <summary>Forwarded by: create_project.</summary>
    public string? Author { get; set; }

    /// <summary>Forwarded by: create_project.</summary>
    public string? Comment { get; set; }

    /// <summary>Forwarded by: save_project_as.</summary>
    public string? TargetDirectory { get; set; }

    /// <summary>Forwarded by: save_project_as.</summary>
    public string? TargetName { get; set; }

    /// <summary>Forwarded by: open_project. The session-rebind escape hatch.</summary>
    public bool ForceRebind { get; set; }

    /// <summary>
    /// Forwarded by: save_project_as. Whether the session rebinds to the saved copy.
    /// Distinct from ForceRebind.
    /// </summary>
    public bool Rebind { get; set; } = true;

    /// <summary>Forwarded by: archive_project.</summary>
    public string? ArchiveDirectory { get; set; }

    /// <summary>Forwarded by: archive_project.</summary>
    public string? ArchiveName { get; set; }

    /// <summary>Forwarded by: archive_project.</summary>
    public string? ArchiveMode { get; set; }

    /// <summary>Forwarded by: archive_project.</summary>
    public bool SaveBeforeArchive { get; set; } = true;

    /// <summary>Forwarded by: close_project.</summary>
    public bool SaveBeforeClose { get; set; } = true;

    #endregion
}
