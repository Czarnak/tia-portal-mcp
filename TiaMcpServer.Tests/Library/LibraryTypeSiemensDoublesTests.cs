using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public class LibraryTypeSiemensDoublesTests
{
    [Fact]
    public void ConcreteCompositionsPreserveFixturesAndRecordDocumentImports()
    {
        var library = new ProjectLibrary();
        var type = library.TypeFolder.AddType("FixtureType");
        var version = type.AddVersion("1.0.0");
        var directory = new DirectoryInfo(Path.GetTempPath());
        var environment = new PlcSoftware().TypeGroup;
        library.TypeFolder.Types.CreateFromDocumentsAction = (_, _, _, _) => new() { CreatedType = type };
        type.Versions.CreateFromDocumentsAction = (_, _, _, _) => new() { CreatedVersion = version };

        Assert.Same(type, library.TypeFolder.Types.CreateFromDocuments(directory, "FixtureType", environment, LibraryImportOptions.None).CreatedType);
        Assert.Same(version, type.Versions.CreateFromDocuments(directory, "FixtureType", CreateOptions.Override, LibraryImportOptions.None).CreatedVersion);
        Assert.Equal((directory, "FixtureType", (IEngineeringObject)environment, LibraryImportOptions.None), Assert.Single(library.TypeFolder.Types.CreateFromDocumentsCalls));
        Assert.Equal((directory, "FixtureType", CreateOptions.Override, LibraryImportOptions.None), Assert.Single(type.Versions.CreateFromDocumentsCalls));
        Assert.Same(type, Assert.Single(library.TypeFolder.Types));
        var failure = new EngineeringException("Synthetic enumeration failure.");
        type.Versions.EnumerationFailure = failure;
        Assert.Same(failure, Assert.Throws<EngineeringException>(() => type.Versions.ToArray()));
    }

    [Fact]
    public void VersionStateDefaultsInstancesAndDeleteHooksAreScriptable()
    {
        var library = new ProjectLibrary();
        var type = library.TypeFolder.AddType("FixtureType");
        var first = type.AddVersion("1.0.0", true);
        var second = type.AddVersion("2.0.0");
        second.State = LibraryTypeVersionState.InWork;
        second.Release(CreateOrReleaseDependenciesMode.DoNotAutomaticallyCreateOrReleaseDependencies, new Version(2, 1, 0), null, "Fixture release");
        Assert.Equal(LibraryTypeVersionState.Committed, second.State);
        Assert.Equal(new Version(2, 1, 0), second.VersionNumber);
        Assert.Single(second.ReleaseCalls);
        second.SetAsDefault();
        Assert.False(first.IsDefault);
        Assert.True(second.IsDefault);

        var plc = new PlcSoftware();
        var block = new FB();
        var link = new LibraryTypeInstanceInfo { LibraryTypeVersion = second, LibraryTypeInstance = block };
        block.LibraryTypeInstanceInfo = link;
        second.Instances[plc] = new[] { link };
        Assert.Same(link, block.GetService<LibraryTypeInstanceInfo>());
        Assert.Same(block, Assert.Single(second.FindInstances(plc)).LibraryTypeInstance);
        Assert.Same(plc, Assert.Single(second.FindInstancesCalls));

        var failure = new EngineeringException("Synthetic native refusal.");
        second.DeleteAction = () => throw failure;
        Assert.Same(failure, Assert.Throws<EngineeringException>(second.Delete));
        Assert.Contains(second, type.Versions.Items);
        second.Discard();
        Assert.DoesNotContain(second, type.Versions.Items);
        type.Delete();
        Assert.Empty(library.TypeFolder.Types.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransactionsRunTheScriptedCommitOrRollbackOnce(bool commit)
    {
        var library = new UserGlobalLibrary();
        var restored = 0;
        var saved = 0;
        using var portal = new TiaPortal
        {
            TransactionAction = transaction =>
            {
                transaction.RollbackAction = () => restored++;
                transaction.CommitAction = () => saved++;
            }
        };
        using var access = portal.ExclusiveAccess("Fixture transaction");
        var transaction = access.Transaction(library, "Fixture mutation");
        Assert.Same(library, transaction.Persistence);
        if (commit) transaction.CommitOnDispose();
        transaction.Dispose();
        transaction.Dispose();
        Assert.Equal(commit, transaction.Committed);
        Assert.Equal(commit ? 0 : 1, restored);
        Assert.Equal(commit ? 1 : 0, saved);
    }

    [Fact]
    public void TypedInstantiationAndSynchronizationRecordArguments()
    {
        var plc = new PlcSoftware();
        var codeVersion = new CodeBlockLibraryTypeVersion();
        var typeVersion = new PlcTypeLibraryTypeVersion();
        var block = new FC();
        var plcType = new PlcType();
        plc.BlockGroup.Blocks.CreateFromVersionAction = (_, _) => block;
        plc.TypeGroup.Types.CreateFromVersionAction = (_, _) => plcType;
        Assert.Same(block, plc.BlockGroup.Blocks.CreateFrom(codeVersion, UpdatePathsMode.ThrowIfPathsConflict));
        Assert.Same(plcType, plc.TypeGroup.Types.CreateFrom(typeVersion, UpdatePathsMode.KeepExistingPathsInTarget));
        Assert.Equal((codeVersion, UpdatePathsMode.ThrowIfPathsConflict), Assert.Single(plc.BlockGroup.Blocks.CreateFromVersionCalls));
        Assert.Equal((typeVersion, UpdatePathsMode.KeepExistingPathsInTarget), Assert.Single(plc.TypeGroup.Types.CreateFromVersionCalls));

        var type = new LibraryType();
        var library = new ProjectLibrary();
        var updated = false;
        type.UpdateLibraryAction = (_, _, _, _) => updated = true;
        type.UpdateLibrary(library, DeleteUnusedVersionsMode.DoNotDelete, StructureConflictResolutionMode.RetainStructure, ForceUpdateMode.NoDefaultVersionChange);
        Assert.True(updated);
        Assert.Equal(((ILibrary)library, DeleteUnusedVersionsMode.DoNotDelete, StructureConflictResolutionMode.RetainStructure, ForceUpdateMode.NoDefaultVersionChange), Assert.Single(type.UpdateLibraryCalls));
    }
}
