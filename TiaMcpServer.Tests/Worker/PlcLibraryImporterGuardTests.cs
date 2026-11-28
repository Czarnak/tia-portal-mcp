using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
using SiemensProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Worker;

public sealed class PlcLibraryImporterGuardTests
{
    public static TheoryData<string, string> Routes => new()
    {
        { "FB", "xml" }, { "FC", "xml" }, { "FB", "simaticSd" }, { "FC", "simaticSd" },
        { "FB", "source" }, { "FC", "source" }, { "UDT", "xml" }, { "UDT", "source" }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void PlainControlsReachRealImportOrGeneration(string kind, string route)
    {
        var fixture = new Fixture(kind, route);

        var error = Record.Exception(fixture.Import);

        Assert.Null(error);
        Assert.Equal(1, fixture.NativeMutationCalls);
        Assert.Equal(route == "source" ? 1 : 0, fixture.SourceCreateCalls);
        Assert.All(fixture.Sources, source => Assert.Equal(1, source.DeleteCalls));
        Assert.Empty(fixture.Plc.ExternalSourceGroup.ExternalSources.Items);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void LinkedInWorkAndCommittedRefuseBeforeRealMutation(string kind, string route)
    {
        foreach (var state in new[] { LibraryTypeVersionState.InWork, LibraryTypeVersionState.Committed })
        {
            var fixture = new Fixture(kind, route);
            fixture.Link(state);

            var error = Record.Exception(fixture.Import);

            Assert.Equal(0, fixture.NativeMutationCalls);
            Assert.Equal(0, fixture.SourceCreateCalls);
            AssertBlocked(error, fixture.IsType);
        }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void LinkAppearingDuringFreshExportRefusesBeforeMutationOrSourceRegistration(string kind, string route)
    {
        var fixture = new Fixture(kind, route);
        fixture.AfterExport = () => fixture.Link(LibraryTypeVersionState.Committed);

        var error = Record.Exception(fixture.Import);

        Assert.Equal(0, fixture.NativeMutationCalls);
        Assert.Equal(0, fixture.SourceCreateCalls);
        AssertBlocked(error, fixture.IsType);
    }

    [Theory]
    [InlineData("FB", false)]
    [InlineData("FC", true)]
    [InlineData("UDT", false)]
    [InlineData("UDT", true)]
    public void LinkAppearingAfterSourceRegistrationRefusesAndCleansUp(string kind, bool nested)
    {
        var fixture = new Fixture(kind, "source", nested);
        fixture.AfterSourceCreated = () => fixture.Link(LibraryTypeVersionState.InWork);

        var error = Record.Exception(fixture.Import);

        Assert.Equal(0, fixture.NativeMutationCalls);
        Assert.Equal(1, fixture.SourceCreateCalls);
        Assert.Equal(1, Assert.Single(fixture.Sources).DeleteCalls);
        Assert.Empty(fixture.Plc.ExternalSourceGroup.ExternalSources.Items);
        Assert.All(fixture.Plc.ExternalSourceGroup.ExternalSources.CreateFromFileCalls, call => Assert.False(File.Exists(call.Path)));
        var failure = AssertBlocked(error, fixture.IsType);
        if (!fixture.IsType) Assert.Equal("removed", failure.BlockImportOutcome!.TemporarySourceState);
    }

    [Fact]
    public void LateSourceGuardRetainsFailedCleanupEvidenceWithoutTargetMutation()
    {
        var fixture = new Fixture("FB", "source");
        fixture.AfterSourceCreated = () => fixture.Link(LibraryTypeVersionState.InWork);
        fixture.SourceDeleteFailure = new EngineeringException("Source cleanup refused.");

        var error = Record.Exception(fixture.Import);

        Assert.Equal(0, fixture.NativeMutationCalls);
        var failure = AssertBlocked(error, isType: false);
        Assert.Equal("residue_possible", failure.BlockImportOutcome!.TemporarySourceState);
        Assert.Single(fixture.Plc.ExternalSourceGroup.ExternalSources.Items);
        Assert.Equal(1, Assert.Single(fixture.Sources).DeleteCalls);
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("simaticSd")]
    public void ReResolvedMissingBlockNeverCreatesThroughOverride(string route)
    {
        var fixture = new Fixture("FB", route);
        fixture.AfterExport = () => fixture.Plc.BlockGroup.Blocks.Items.Clear();

        var error = Record.Exception(fixture.Import);

        Assert.Equal(0, fixture.NativeMutationCalls);
        var failure = Assert.IsType<WorkerOperationException>(error);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, failure.FailureCategory);
        Assert.Equal("not_started", failure.BlockImportOutcome!.ImportStage);
    }

    [Theory]
    [InlineData("FB", "xml", false)]
    [InlineData("FB", "source", false)]
    [InlineData("UDT", "xml", false)]
    [InlineData("UDT", "source", false)]
    [InlineData("FB", "xml", true)]
    [InlineData("FB", "source", true)]
    [InlineData("UDT", "xml", true)]
    [InlineData("UDT", "source", true)]
    public void InitialGuardFatalLossEscapesOriginalBeforeMutation(string kind, string route, bool wrapped)
    {
        var fixture = new Fixture(kind, route);
        var failure = Fatal(wrapped);
        fixture.Target.LibraryTypeInstanceInfoFailure = failure;

        var actual = Record.Exception(fixture.Import);

        Assert.Equal(0, fixture.NativeMutationCalls);
        Assert.Equal(0, fixture.SourceCreateCalls);
        Assert.Same(failure, actual);
    }

    [Theory]
    [InlineData("FB", false)]
    [InlineData("UDT", false)]
    [InlineData("FB", true)]
    [InlineData("UDT", true)]
    public void AdjacentSourceGuardFatalLossEscapesOriginalAfterCleanup(string kind, bool wrapped)
    {
        var fixture = new Fixture(kind, "source");
        var failure = Fatal(wrapped);
        fixture.AfterSourceCreated = () => fixture.Target.LibraryTypeInstanceInfoFailure = failure;

        var actual = Record.Exception(fixture.Import);

        Assert.Equal(0, fixture.NativeMutationCalls);
        Assert.Equal(1, fixture.SourceCreateCalls);
        Assert.Equal(1, Assert.Single(fixture.Sources).DeleteCalls);
        Assert.Empty(fixture.Plc.ExternalSourceGroup.ExternalSources.Items);
        Assert.Same(failure, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdjacentFatalGuardCleansXmlStagingAndEscapesOriginal(bool wrapped)
    {
        var target = new FB { LibraryTypeInstanceInfoFailure = Fatal(wrapped) };
        var failure = target.LibraryTypeInstanceInfoFailure;
        string? staging = null;

        var actual = Record.Exception(() => BlockImportCoordinator.Execute("Object.xml", "<Document />",
            (_, _, _) => PlcLibraryInstanceGuard.RequireAllowed(PlcLibraryInstanceGuard.InspectContentUpdate(target)),
            _ => throw new InvalidOperationException("A refused import must not verify."),
            createStagingDirectory: path => { staging = path; Directory.CreateDirectory(path); }));

        Assert.NotNull(staging);
        Assert.False(Directory.Exists(staging));
        Assert.Same(failure, actual);
    }

    private static Exception Fatal(bool wrapped) => wrapped
        ? new WorkerOperationException(WorkerFailureCategories.PortalLost, "Portal lost during protection inspection.")
        : new NonRecoverableException("Portal lost during protection inspection.");

    private static WorkerOperationException AssertBlocked(Exception? error, bool isType)
    {
        var failure = Assert.IsType<WorkerOperationException>(error);
        Assert.Equal(WorkerFailureCategories.GuardBlocked, failure.FailureCategory);
        Assert.Contains(PlcLibraryInstanceGuard.LibraryInstanceEdit, failure.Message);
        if (!isType)
        {
            Assert.Equal("not_started", failure.BlockImportOutcome!.ImportStage);
            Assert.False(failure.BlockImportOutcome.TargetMutationCommitted);
        }
        return failure;
    }

    private sealed class SuccessCompiler : ICompilable
    {
        public CompilerResult Compile() => new() { State = CompilerResultState.Success };
    }

    private sealed class Fixture
    {
        private readonly string route;
        private readonly string content;
        private readonly string expectedHash;
        private readonly string path;
        private readonly PlcBlockGroup blockGroup;
        private readonly PlcTypeGroup typeGroup;
        public SiemensProject Project { get; } = new() { Path = new FileInfo(@"C:\fixture\fixture.ap21") };
        public PlcSoftware Plc { get; } = new() { Name = "PLC", CompilerService = new SuccessCompiler() };
        public NamedObject Target { get; }
        public bool IsType { get; }
        public Action? AfterExport { get; set; }
        public Action? AfterSourceCreated { get; set; }
        public Exception? SourceDeleteFailure { get; set; }
        public List<PlcExternalSource> Sources { get; } = new();
        public int SourceCreateCalls => Plc.ExternalSourceGroup.ExternalSources.CreateFromFileCalls.Count;
        public int NativeMutationCalls => blockGroup.Blocks.ImportCalls.Count + blockGroup.Blocks.ImportFromDocumentsCalls.Count
            + typeGroup.Types.ImportCalls.Count + Sources.Sum(source => source.GenerateCalls);

        public Fixture(string kind, string route, bool nested = false)
        {
            this.route = route;
            IsType = kind == "UDT";
            Target = kind switch { "FB" => new FB(), "FC" => new FC(), _ => new PlcType() };
            Target.Name = "Object";
            var device = new Device { Name = "Station" };
            device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = Plc } });
            Project.Devices.Items.Add(device);
            blockGroup = nested ? Plc.BlockGroup.Groups.Create("Nested") : Plc.BlockGroup;
            var typeChild = new PlcTypeUserGroup { Name = "Nested" };
            if (nested) Plc.TypeGroup.Groups.Items.Add(typeChild);
            typeGroup = nested ? typeChild : Plc.TypeGroup;
            path = "PLC/" + (IsType ? "Types/" : "Blocks/") + (nested ? "Nested/" : "") + "Object";
            var xml = $"<Document><{(IsType ? "SW.Types.PlcStruct" : "SW.Blocks." + kind)}><AttributeList><Name>Object</Name></AttributeList></{(IsType ? "SW.Types.PlcStruct" : "SW.Blocks." + kind)}></Document>";
            var sourceText = kind switch
            {
                "FB" => "FUNCTION_BLOCK \"Object\"\nBEGIN\nEND_FUNCTION_BLOCK",
                "FC" => "FUNCTION \"Object\" : Void\nBEGIN\nEND_FUNCTION",
                _ => "TYPE \"Object\"\nSTRUCT\nField : Bool;\nEND_STRUCT;\nEND_TYPE"
            };
            Plc.ExternalSourceGroup.GenerateSourceAction = (_, file, _) => { File.WriteAllText(file.FullName, sourceText); AfterExport?.Invoke(); };
            Plc.ExternalSourceGroup.ExternalSources.CreateFromFileAction = (_, _) =>
            {
                var source = new PlcExternalSource { GenerateAction = (_, _) => new List<IEngineeringObject> { Target } };
                source.DeleteAction = () =>
                {
                    if (SourceDeleteFailure is not null) throw SourceDeleteFailure;
                    Plc.ExternalSourceGroup.ExternalSources.Items.Remove(source);
                };
                Sources.Add(source);
                AfterSourceCreated?.Invoke();
                return source;
            };
            if (IsType)
            {
                var type = (PlcType)Target;
                typeGroup.Types.Items.Add(type);
                type.ExportAction = (file, _) => { File.WriteAllText(file.FullName, xml); AfterExport?.Invoke(); };
                content = route == "source" ? sourceText : xml;
                expectedHash = ContentHashRules.Compute(route == "source" ? "source" : "xml", PlcTypeExporter.Export(Project, path, route == "source" ? "source" : "xml"));
            }
            else
            {
                var block = (PlcBlock)Target;
                block.CompilerService = new SuccessCompiler();
                blockGroup.Blocks.Items.Add(block);
                block.ExportAction = (file, _) =>
                {
                    if (route == "simaticSd") throw new EngineeringNotSupportedException("Companion-only export.");
                    File.WriteAllText(file.FullName, xml);
                    AfterExport?.Invoke();
                };
                block.ExportAsDocumentsAction = (directory, name) =>
                {
                    var document = new FileInfo(Path.Combine(directory.FullName, name + ".s7dcl"));
                    File.WriteAllText(document.FullName, sourceText);
                    AfterExport?.Invoke();
                    return new() { State = DocumentResultState.Success, ExportedDocuments = new List<FileInfo> { document } };
                };
                var exported = BlockExporter.Export(Project, path, route == "source" ? "source" : "xml");
                content = exported;
                expectedHash = ContentHashRules.Compute(route == "source" ? "source" : "xml", exported);
            }
        }

        public void Link(LibraryTypeVersionState state)
            => Target.LibraryTypeInstanceInfo = new LibraryTypeInstanceInfo { LibraryTypeVersion = new LibraryTypeVersion { State = state } };

        public void Import()
        {
            if (IsType) PlcTypeImporter.Import(Project, path, content, route == "source" ? "source" : "xml", expectedHash);
            else BlockImporter.Import(Project, path, content, route == "source" ? "source" : "xml", expectedHash);
        }
    }
}
