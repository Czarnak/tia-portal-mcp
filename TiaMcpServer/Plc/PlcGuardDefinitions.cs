using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>The plc_write guards (spec §4.2). Only block and info severities exist: nothing asks the user.</summary>
public static class PlcGuardDefinitions
{
    public const string StateUnverifiable = "plc_state_unverifiable";
    public const string NameCollision = "plc_name_collision";
    public const string BlockExists = "plc_block_exists";
    public const string DefaultTagTable = "plc_default_tag_table";
    public const string AttributeUnreadable = "plc_attribute_unreadable";
    public const string DeletesBlock = "plc_deletes_block";
    public const string DeletesGroupContents = "plc_deletes_group_contents";
    public const string DeletesTableContents = "plc_deletes_table_contents";
    public const string AddressOverlap = "plc_address_overlap";
    public const string ObSingletonExists = "plc_ob_singleton_exists";

    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = Validate(new[]
    {
        new WriteGuardDefinition(StateUnverifiable, WriteGuardSeverities.Block, "PLC evidence relevant to the target is incomplete or unreadable; unreadable evidence is never treated as empty."),
        new WriteGuardDefinition(NameCollision, WriteGuardSeverities.Block, "A new or renamed PLC object collides with an existing or in-call object."),
        new WriteGuardDefinition(BlockExists, WriteGuardSeverities.Block, "create_block targets an existing block; existing blocks are never overwritten."),
        new WriteGuardDefinition(DefaultTagTable, WriteGuardSeverities.Block, "The default tag table cannot be deleted."),
        new WriteGuardDefinition(AttributeUnreadable, WriteGuardSeverities.Block, "A requested external-access flag is unreadable on the current tag."),
        new WriteGuardDefinition(ObSingletonExists, WriteGuardSeverities.Block, "create_block targets a singleton OB event class whose number an existing or in-call OB already holds."),
        new WriteGuardDefinition(DeletesBlock, WriteGuardSeverities.Info, "Deleting the block removes it and its content."),
        new WriteGuardDefinition(DeletesGroupContents, WriteGuardSeverities.Info, "Deleting the block group removes every block and group inside it."),
        new WriteGuardDefinition(DeletesTableContents, WriteGuardSeverities.Info, "Deleting the tag table removes its tags and user constants."),
        new WriteGuardDefinition(AddressOverlap, WriteGuardSeverities.Info, "The requested logical address is already used by another tag; TIA Portal allows the overlap."),
    });

    public static IReadOnlyList<WriteGuardDefinition> Validate(IEnumerable<WriteGuardDefinition> definitions)
    {
        var materialized = definitions.ToArray();
        if (materialized.Any(guard => guard.Severity == WriteGuardSeverities.Acknowledge))
            throw new ArgumentException("PLC guards cannot require acknowledgement.", nameof(definitions));
        _ = new WriteGuardCatalog(materialized);
        return Array.AsReadOnly(materialized);
    }
}
