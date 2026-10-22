using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Vci;

public static class VciGuardDefinitions
{
    public const string FileKeyCollision = "vci_file_key_collision";
    public const string NameNotRepresentable = "vci_name_not_representable";
    public const string WorkspaceExists = "vci_workspace_exists";
    public const string StateUnverifiable = "vci_state_unverifiable";
    public const string FilesRemain = "vci_files_remain";
    public const string ImportUnvalidated = "vci_import_unvalidated";
    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = WriteGuardCatalog.NonInteractive(new[]
    {
        new WriteGuardDefinition(FileKeyCollision, WriteGuardSeverities.Block, "An export target file or another mapping already uses a file key."),
        new WriteGuardDefinition(NameNotRepresentable, WriteGuardSeverities.Block, "A mirrored name or path cannot be represented on disk."),
        new WriteGuardDefinition(WorkspaceExists, WriteGuardSeverities.Block, "An existing workspace uses the requested name or root."),
        new WriteGuardDefinition(StateUnverifiable, WriteGuardSeverities.Block, "Required scope or mapping evidence could not be read."),
        new WriteGuardDefinition(FilesRemain, WriteGuardSeverities.Info, "Deleting a workspace or mapping leaves its files on disk."),
        new WriteGuardDefinition(ImportUnvalidated, WriteGuardSeverities.Info, "Imported content is not validated; run compile_check."),
    }, "VCI");
}
