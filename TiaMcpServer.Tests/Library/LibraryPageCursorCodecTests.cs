using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;
using TiaMcpServer.Library;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryPageCursorCodecTests
{
    private static readonly byte[] TestKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] OtherKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private const string TypeGuid = "11111111-1111-1111-1111-111111111111";

    private static LibraryReadItem Item(Action<LibraryReadItem>? configure = null)
    {
        var item = new LibraryReadItem
        {
            OperationId = "op", Operation = "list_children", Scope = "global",
            LibraryName = "Fixture", FolderKind = "types",
        };
        configure?.Invoke(item);
        return item;
    }

    private static LibraryPageCursorState State(LibraryReadItem item, ProjectBindingSnapshot binding, int offset = 2) => new(
        "library-s1", item.Operation, LibraryPageCursorCodec.QueryHash(item), offset,
        ProjectBindingCursorState.FromSnapshot(binding));

    private static (OpennessWorkerClient Client, ProjectSessionBinding Binding, TempAuditDirectory Directory) SyntheticClient()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(new WorkerSessionIdentity
        {
            WorkerSessionId = "synthetic-worker", SessionGeneration = 1, PortalProcessId = 42,
            ProjectPath = "C:/Projects/Local.amc21",
            Context = new ProjectContextInfo
            {
                ContainerKind = ProjectContainerKinds.LocalSession,
                SessionMode = MultiuserSessionModes.Unknown,
                EngineeringProjectPath = "C:/Projects/Local.amc21",
                Capabilities = ProjectCapabilityCatalog.Describe(ProjectContainerKinds.LocalSession).ToList()
            }
        }, forceRebind: false, out var error), error);
        var directory = new TempAuditDirectory();
        // The worker must never start: every cursor rejection happens before dispatch.
        var client = new OpennessWorkerClient(binding,
            workerExecutablePath: Path.Combine(directory.Path, "missing-worker.exe"),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));
        return (client, binding, directory);
    }

    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");
        var codec = new LibraryPageCursorCodec(protector);
        var (client, _, directory) = SyntheticClient();
        using var _ = client;
        using var __ = directory;
        var state = State(Item(), client.BindingSnapshot);

        Assert.Equal(state, codec.Decode(codec.Encode(state)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tampered_OtherKey_InvalidCursor(bool otherKey)
    {
        using var issuer = new AuthenticatedCursorProtector(TestKey, "process-a");
        using var reader = new AuthenticatedCursorProtector(otherKey ? OtherKey : TestKey, "process-b");
        var (client, _, directory) = SyntheticClient();
        using var _ = client;
        using var __ = directory;
        var cursor = new LibraryPageCursorCodec(issuer).Encode(State(Item(), client.BindingSnapshot));
        var tampered = cursor[..^2] + (cursor[^2] == 'A' ? "B" : "A") + cursor[^1];

        var error = Assert.Throws<CursorException>(() => new LibraryPageCursorCodec(otherKey ? reader : issuer).Decode(otherKey ? cursor : tampered));

        Assert.Equal(WorkerFailureCategories.InvalidCursor, error.Category);
        Assert.Equal("The supplied library_read cursor is invalid.", error.Message);
    }

    [Fact]
    public void Decode_StateForNonPagedOperation_IsInvalidCursor()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");
        var codec = new LibraryPageCursorCodec(protector);
        var (client, _, directory) = SyntheticClient();
        using var _ = client;
        using var __ = directory;
        var inspect = Item(i => { i.Operation = "inspect_library"; i.FolderKind = null; });

        Assert.ThrowsAny<Exception>(() => codec.Encode(State(inspect, client.BindingSnapshot)));
    }

    [Fact]
    public void QueryHash_ExcludesPaging()
    {
        var plain = Item();
        var paged = Item(i => { i.Offset = 5; i.Limit = 7; i.Cursor = "ignored"; });

        Assert.Equal(LibraryPageCursorCodec.QueryHash(plain), LibraryPageCursorCodec.QueryHash(paged));
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("scope")]
    [InlineData("libraryPath")]
    [InlineData("libraryName")]
    [InlineData("folderKind")]
    [InlineData("folderPath")]
    [InlineData("typeGuid")]
    [InlineData("typeName")]
    [InlineData("versionGuid")]
    [InlineData("versionNumber")]
    [InlineData("direction")]
    public void QueryHash_DiffersByEachSelector(string field)
    {
        var baseline = LibraryPageCursorCodec.QueryHash(Item());
        var changed = Item(i =>
        {
            switch (field)
            {
                case "operation": i.Operation = "list_versions"; break;
                case "scope": i.Scope = "project"; break;
                case "libraryPath": i.LibraryPath = @"C:\Libs\Fixture.al21"; break;
                case "libraryName": i.LibraryName = "Other"; break;
                case "folderKind": i.FolderKind = "masterCopies"; break;
                case "folderPath": i.FolderPath = new List<string> { "A" }; break;
                case "typeGuid": i.TypeGuid = TypeGuid; break;
                case "typeName": i.TypeName = "Motor"; break;
                case "versionGuid": i.VersionGuid = TypeGuid; break;
                case "versionNumber": i.VersionNumber = "1.0.0"; break;
                default: i.Direction = "dependents"; break;
            }
        });

        Assert.NotEqual(baseline, LibraryPageCursorCodec.QueryHash(changed));
    }

    [Fact]
    public void QueryHash_EquivalentDefaults_HashEqual()
    {
        var omitted = Item(i => { i.Operation = "list_dependencies"; i.TypeGuid = TypeGuid; i.VersionGuid = TypeGuid; });
        var explicitDefault = Item(i => { i.Operation = "list_dependencies"; i.TypeGuid = TypeGuid; i.VersionGuid = TypeGuid; i.Direction = "dependencies"; });
        var dependents = Item(i => { i.Operation = "list_dependencies"; i.TypeGuid = TypeGuid; i.VersionGuid = TypeGuid; i.Direction = "dependents"; });

        Assert.Equal(LibraryPageCursorCodec.QueryHash(omitted), LibraryPageCursorCodec.QueryHash(explicitDefault));
        Assert.NotEqual(LibraryPageCursorCodec.QueryHash(omitted), LibraryPageCursorCodec.QueryHash(dependents));
        Assert.Equal(LibraryPageCursorCodec.QueryHash(Item()), LibraryPageCursorCodec.QueryHash(Item(i => i.FolderPath = new List<string>())));
    }

    [Fact]
    public void QueryHash_DistinguishesFolderPathSegmentsFromJoinedName()
    {
        var split = Item(i => i.FolderPath = new List<string> { "A", "B" });
        var joined = Item(i => i.FolderPath = new List<string> { "A/B" });

        Assert.NotEqual(LibraryPageCursorCodec.QueryHash(split), LibraryPageCursorCodec.QueryHash(joined));
    }

    public enum Rejection { GarbageCursor, OtherOperation, OtherSelectors, BindingRevisionChanged }

    [Theory]
    [InlineData(Rejection.GarbageCursor, WorkerFailureCategories.InvalidCursor)]
    [InlineData(Rejection.OtherOperation, WorkerFailureCategories.CursorFilterMismatch)]
    [InlineData(Rejection.OtherSelectors, WorkerFailureCategories.CursorFilterMismatch)]
    [InlineData(Rejection.BindingRevisionChanged, WorkerFailureCategories.CursorBindingMismatch)]
    public async Task CursorFailures_AreNotSent(Rejection rejection, string expectedCategory)
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");
        var codec = new LibraryPageCursorCodec(protector);
        var (client, binding, directory) = SyntheticClient();
        using var _ = client;
        using var __ = directory;
        var issuedFor = Item();
        var cursor = rejection == Rejection.GarbageCursor
            ? "garbage"
            : codec.Encode(State(issuedFor, client.BindingSnapshot));
        var presented = rejection switch
        {
            Rejection.OtherOperation => Item(i => { i.Operation = "list_versions"; i.FolderKind = null; i.TypeGuid = TypeGuid; }),
            Rejection.OtherSelectors => Item(i => i.LibraryName = "Other"),
            _ => Item(),
        };
        presented.Cursor = cursor;
        if (rejection == Rejection.BindingRevisionChanged) binding.Invalidate("The connection was lost.");

        var result = await LibraryWorkerInvoker.InvokeReadAsync(client, codec, presented);

        Assert.False(result.Success);
        Assert.Equal(expectedCategory, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
    }

    [Fact]
    public async Task ValidCursor_ForSameOperationAndSelectors_PassesTheHostChecks()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");
        var codec = new LibraryPageCursorCodec(protector);
        var (client, _, directory) = SyntheticClient();
        using var _ = client;
        using var __ = directory;
        var presented = Item();
        presented.Cursor = codec.Encode(State(presented, client.BindingSnapshot));

        // Read-only access still allows the read, so the call proceeds to dispatch; the missing worker
        // executable makes it fail after the host checks, never with a cursor category.
        var result = await LibraryWorkerInvoker.InvokeReadAsync(client, codec, presented);

        Assert.DoesNotContain(result.FailureCategory, new[]
        {
            WorkerFailureCategories.InvalidCursor, WorkerFailureCategories.CursorFilterMismatch,
            WorkerFailureCategories.CursorBindingMismatch,
        });
    }
}
