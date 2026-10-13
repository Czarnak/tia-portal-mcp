using TiaMcpServer.Safety.Pipeline;
namespace TiaMcpServer.Hmi;

public static class HmiGuardDefinitions
{
    public const string NameCollision = "hmi_name_collision";
    public const string StateUnverifiable = "hmi_state_unverifiable";
    public const string DeletesTableContents = "hmi_deletes_table_contents";
    public const string DeletesReferencedTag = "hmi_deletes_referenced_tag";
    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new WriteGuardDefinition(NameCollision, WriteGuardSeverities.Block, "A create or rename collides with an HMI object in its name scope."),
        new WriteGuardDefinition(StateUnverifiable, WriteGuardSeverities.Block, "Required HMI evidence is incomplete or exceeds the cross-reference query limit."),
        new WriteGuardDefinition(DeletesTableContents, WriteGuardSeverities.Info, "Deleting the tag table also deletes its tags."),
        new WriteGuardDefinition(DeletesReferencedTag, WriteGuardSeverities.Info, "Deleting a referenced tag leaves its references dangling."),
    });
}
