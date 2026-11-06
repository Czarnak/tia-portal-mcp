using TiaMcpServer.Library;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryGuardDefinitionsTests
{
    [Fact]
    public void AllGuardsAreRegisteredWithNonInteractiveSeverity()
    {
        var definitions = LibraryGuardDefinitions.Definitions.ToDictionary(d => d.Id, d => d.Severity);
        Assert.Equal(4, definitions.Count);
        Assert.Equal(WriteGuardSeverities.Block, definitions[LibraryGuardDefinitions.MasterCopyExists]);
        Assert.Equal(WriteGuardSeverities.Block, definitions[LibraryGuardDefinitions.StateUnverifiable]);
        Assert.Equal(WriteGuardSeverities.Info, definitions[LibraryGuardDefinitions.DiscardsChanges]);
        Assert.Equal(WriteGuardSeverities.Info, definitions[LibraryGuardDefinitions.DeletesFolderContents]);
        Assert.All(definitions.Values, severity => Assert.NotEqual(WriteGuardSeverities.Acknowledge, severity));
    }
}
