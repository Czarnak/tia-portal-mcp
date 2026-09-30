using Xunit;

namespace TiaMcpServer.Tests.Block;

/// <summary>Static wiring evidence only; live Siemens behavior remains unverified.</summary>
public class BlockImporterRouteSourceTests
{
    [Fact]
    public void AllThreeTargetCallsAreBracketedInOrderAndUseOneOutcomeRunner()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "BlockImporter.cs");

        AssertBracketed(source,
            "target.Group.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);",
            "boundary.RecordReturnedResult(BlockImportReturnedState.Success);");
        AssertBracketed(source,
            "target.Group.Blocks.ImportFromDocuments(",
            "boundary.RecordReturnedResult(result.State == DocumentResultState.Success");
        AssertBracketed(source,
            "scope.Source.GenerateBlocksFromSource(preflight.Target.UserGroup, GenerateBlockOption.None);",
            "boundary.RecordReturnedResult(BlockImportReturnedState.Success);");
        AssertBracketed(source,
            "scope.Source.GenerateBlocksFromSource(GenerateBlockOption.None);",
            "boundary.RecordReturnedResult(BlockImportReturnedState.Success);");

        Assert.Contains("BlockImportCoordinator.Execute(", source, StringComparison.Ordinal);
        Assert.Contains("BlockImportCoordinator.ExecuteSource(", source, StringComparison.Ordinal);
        var coordinator = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "BlockImportCoordinator.cs");
        Assert.Equal(1, Count(coordinator, "private static BlockImportResult RunOutcome"));
        Assert.Equal(3, Count(source, "boundary.BeforeSiemensCall();"));
        Assert.Equal(3, Count(source, "boundary.AfterSiemensCallReturned();"));
    }

    [Fact]
    public void ExternalSourceLifecycleCallbacksBracketCreationAndRecordDeletion()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "ExternalSourceScope.cs");

        AssertBefore(source, "observer?.BeforeCreateFromFile();", "ExternalSources.CreateFromFile(");
        AssertBefore(source, "ExternalSources.CreateFromFile(", "observer?.AfterCreateFromFileReturned();");
        Assert.Contains("observer?.AfterDeleteAttempt(ProjectNodeRemoved);", source, StringComparison.Ordinal);
        Assert.Contains("BlockSourceArtifactTracker? observer = null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileObservedPreservesBlockNamedAndAllPlcSelectionWithoutProseParsing()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "CompileChecker.cs");

        Assert.Contains("CompileObserved(", source, StringComparison.Ordinal);
        Assert.Contains("Project project,", source, StringComparison.Ordinal);
        Assert.Contains("string? plcName,", source, StringComparison.Ordinal);
        Assert.Contains("string? blockPath)", source, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(blockPath)", source, StringComparison.Ordinal);
        Assert.Contains("PlcSoftwareLocator.FindAll(project, address.PlcName).FirstOrDefault()", source, StringComparison.Ordinal);
        Assert.Contains("PlcSoftwareLocator.FindAll(project, plcName)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DiagnosticNotes.Any", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Contains(\"Compilation failed\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void XmlAndSourceRoutesUseTheCommonFreshFinalReader()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "BlockImporter.cs");

        Assert.Equal(2, Count(source, "compileAllowed => ObservePostconditions("));
        Assert.Equal(1, Count(source, "return ObserveFinalState("));
        Assert.Equal(1, Count(source,
            "private static BlockPostconditionEvidence ObserveFinalState("));
        Assert.Contains("BlockTargetResolver.ResolveForImport(project, address)", source, StringComparison.Ordinal);
        Assert.Contains("BlockExporter.VerifyPrimaryDocument(", source, StringComparison.Ordinal);
        Assert.Contains("BlockExporter.Export(project, blockPath, SourceFormatNames.Source)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void XmlAndSourcePreflightDecorationEndsBeforeCoordinatorEntry()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "BlockImporter.cs");

        Assert.Equal(2, Count(source, "BlockImportCoordinator.ExecuteWithPreTargetOutcome("));

        var xmlWrapper = source.IndexOf(
            "BlockImportCoordinator.ExecuteWithPreTargetOutcome(", StringComparison.Ordinal);
        var xmlPreflight = source.IndexOf(
            "BlockWritePreflight.PrepareUpdate(", xmlWrapper, StringComparison.Ordinal);
        var xmlWrapperEnd = source.IndexOf(
            "sourceApplicable: false);", xmlPreflight, StringComparison.Ordinal);
        var xmlCoordinator = source.IndexOf(
            "return BlockImportCoordinator.Execute(", xmlWrapperEnd, StringComparison.Ordinal);
        Assert.True(xmlWrapper >= 0 && xmlWrapper < xmlPreflight);
        Assert.True(xmlPreflight < xmlWrapperEnd && xmlWrapperEnd < xmlCoordinator);

        var sourceWrapper = source.IndexOf(
            "BlockImportCoordinator.ExecuteWithPreTargetOutcome(", xmlWrapper + 1,
            StringComparison.Ordinal);
        var sourcePreflight = source.IndexOf(
            "PlcTypeSourcePreflight.TryReadDeclaredName(", sourceWrapper, StringComparison.Ordinal);
        var sourceWrapperEnd = source.IndexOf(
            "sourceApplicable: true);", sourcePreflight, StringComparison.Ordinal);
        var sourceCoordinator = source.IndexOf(
            "return BlockImportCoordinator.ExecuteSource(", sourceWrapperEnd,
            StringComparison.Ordinal);
        Assert.True(sourceWrapper > xmlCoordinator && sourceWrapper < sourcePreflight);
        Assert.True(sourcePreflight < sourceWrapperEnd && sourceWrapperEnd < sourceCoordinator);
    }

    [Fact]
    public void OutcomeProjectionBudgetIncludesNullsLikeTheHostValidator()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "BlockImportOutcomeProjection.cs");

        Assert.Contains("PropertyNamingPolicy = JsonNamingPolicy.CamelCase", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultIgnoreCondition", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonIgnoreCondition.WhenWritingNull", source, StringComparison.Ordinal);
    }

    private static void AssertBracketed(string source, string call, string resultMarker)
    {
        Assert.Equal(1, Count(source, call));
        var callIndex = source.IndexOf(call, StringComparison.Ordinal);
        Assert.True(callIndex >= 0, $"Expected target call '{call}'.");

        var beforeIndex = source.LastIndexOf("boundary.BeforeSiemensCall();", callIndex, StringComparison.Ordinal);
        var returnedIndex = source.IndexOf("boundary.AfterSiemensCallReturned();", callIndex, StringComparison.Ordinal);
        var resultIndex = source.IndexOf(resultMarker, returnedIndex, StringComparison.Ordinal);
        Assert.True(beforeIndex >= 0 && beforeIndex < callIndex);
        Assert.True(returnedIndex > callIndex);
        Assert.True(resultIndex > returnedIndex);
    }

    private static void AssertBefore(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Expected '{first}'.");
        Assert.True(secondIndex >= 0, $"Expected '{second}'.");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string ReadRepositorySource(params string[] pathSegments)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TiaMcpServer.slnx")))
            root = root.Parent;

        if (root is null)
            throw new DirectoryNotFoundException("Could not locate the repository root.");

        return File.ReadAllText(Path.Combine(new[] { root.FullName }.Concat(pathSegments).ToArray()))
            .Replace("\r\n", "\n");
    }
}
