using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Library;

public static class LibraryGuardDefinitions
{
    public const string MasterCopyExists = "library_master_copy_exists";
    public const string StateUnverifiable = "library_state_unverifiable";
    public const string TypeNameExists = "library_type_name_exists";
    public const string SourceIsTypeInstance = "library_source_is_type_instance";
    public const string VersionNotHigher = "library_version_not_higher";
    public const string DiscardsChanges = "library_discards_changes";
    public const string DeletesFolderContents = "library_deletes_folder_contents";

    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = WriteGuardCatalog.NonInteractive(new[]
    {
        new WriteGuardDefinition(MasterCopyExists, WriteGuardSeverities.Block, "A master copy with this name already exists in the destination folder."),
        new WriteGuardDefinition(StateUnverifiable, WriteGuardSeverities.Block, "Required library state could not be established."),
        new WriteGuardDefinition(TypeNameExists, WriteGuardSeverities.Block, "A type with this name already exists in the project library."),
        new WriteGuardDefinition(SourceIsTypeInstance, WriteGuardSeverities.Block, "The PLC source is already a library type instance."),
        new WriteGuardDefinition(VersionNotHigher, WriteGuardSeverities.Block, "The release number must exceed every released version."),
        new WriteGuardDefinition(DiscardsChanges, WriteGuardSeverities.Info, "Closing without saving discards any unsaved library changes."),
        new WriteGuardDefinition(DeletesFolderContents, WriteGuardSeverities.Info, "Deleting this folder also deletes its contents."),
    }, "Library");
}
