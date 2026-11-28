using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>The plc_write guards (spec §4.2). Only block and info severities exist: nothing asks the user.</summary>
public static class PlcGuardDefinitions
{
    public const string StateUnverifiable = "plc_state_unverifiable";
    public const string DeletesBlock = "plc_deletes_block";
    public const string DeletesGroupContents = "plc_deletes_group_contents";
    public const string DeletesTableContents = "plc_deletes_table_contents";
    public const string AddressOverlap = "plc_address_overlap";
    public const string ObSingletonExists = "plc_ob_singleton_exists";
    public const string LibraryInstanceEdit = "plc_library_instance_edit";
    public const string LibraryTestInstanceDelete = "plc_library_test_instance_delete";

    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = WriteGuardCatalog.NonInteractive(new[]
    {
        new WriteGuardDefinition(StateUnverifiable, WriteGuardSeverities.Block, "The PLC inventory is incomplete, the update or delete target or what a delete removes is unreadable, the user-constant value an update replaces is unreadable, the content cannot be exported, or required library-instance protection cannot be verified; unreadable evidence is never treated as empty."),
        new WriteGuardDefinition(ObSingletonExists, WriteGuardSeverities.Block, "create_block targets a singleton OB event class whose number an existing or in-call OB already holds."),
        new WriteGuardDefinition(LibraryInstanceEdit, WriteGuardSeverities.Block, "Content replacement of a linked PLC library instance is blocked."),
        new WriteGuardDefinition(LibraryTestInstanceDelete, WriteGuardSeverities.Block, "Deleting an in-work library test instance, directly or inside a group, is blocked."),
        new WriteGuardDefinition(DeletesBlock, WriteGuardSeverities.Info, "Deleting the block removes it and its content."),
        new WriteGuardDefinition(DeletesGroupContents, WriteGuardSeverities.Info, "Deleting the block group removes every block and group inside it."),
        new WriteGuardDefinition(DeletesTableContents, WriteGuardSeverities.Info, "Deleting the tag table removes its tags and user constants."),
        new WriteGuardDefinition(AddressOverlap, WriteGuardSeverities.Info, "The requested logical address is already used by another tag; TIA Portal allows the overlap."),
    }, "PLC");
}
