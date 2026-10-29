using TiaMcpServer.Safety.Pipeline;
namespace TiaMcpServer.Hmi;

public static class HmiGuardDefinitions
{
    public const string StateUnverifiable = "hmi_state_unverifiable";
    public const string DeletesTableContents = "hmi_deletes_table_contents";
    public const string DeletesReferencedTag = "hmi_deletes_referenced_tag";
    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = WriteGuardCatalog.NonInteractive(new[]
    {
        new WriteGuardDefinition(StateUnverifiable, WriteGuardSeverities.Block, "Delete-consequence evidence or an update/delete target could not be read completely."),
        new WriteGuardDefinition(DeletesTableContents, WriteGuardSeverities.Info, "Deleting the tag table also deletes its tags."),
        new WriteGuardDefinition(DeletesReferencedTag, WriteGuardSeverities.Info, "Deleting a referenced tag leaves its references dangling."),
    }, "HMI");
}
