using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectTreeWorkerPayloadContractTests
{
    [Fact]
    public void Decode_AcceptsTheDeclaredSnapshotAndCopiesBoundaryValues()
    {
        var requested = Selector((ProjectTreeNodeTypes.Device, "plc_1"));
        var payload = Payload(
            Selector((ProjectTreeNodeTypes.Device, "PLC_1")),
            depth: 2,
            Node("PLC_1", ProjectTreeNodeTypes.Device));
        var warnings = new List<string> { "observed warning" };

        var observed = ProjectTreeWorkerPayloadContract.Decode(
            SuccessfulWorker(payload, @" C:\Projects\Plant.ap21 ", warnings),
            requested,
            requestedDepth: 2);

        Assert.Equal(@"C:\Projects\Plant.ap21", observed.ResolvedProjectPath);
        Assert.Equal(2, observed.Depth);
        Assert.Equal("PLC_1", Assert.Single(observed.Roots).Name);
        Assert.Equal("PLC_1", Assert.Single(observed.CanonicalStartSelector!).Name);
        Assert.Equal(new[] { "observed warning" }, observed.Warnings);
        Assert.NotSame(warnings, observed.Warnings);
    }

    // Production bug caught: a legacy untyped payload could otherwise bypass the v3 result contract.
    [Fact]
    public void Decode_LegacyBareArrayFailsClosedWithoutEchoingPayload()
    {
        var worker = WorkerCallResult.Ok("[{\"name\":\"secret-marker\",\"nodeType\":\"Device\"}]") with
        {
            ResolvedProjectPath = @"C:\Projects\Plant.ap21"
        };

        var error = Assert.Throws<ProjectTreeProtocolException>(() =>
            ProjectTreeWorkerPayloadContract.Decode(worker, requestedSelector: null, requestedDepth: null));

        Assert.Equal(WorkerFailureCategories.ProtocolError, error.Category);
        Assert.DoesNotContain("secret-marker", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MalformedPayloads))]
    public void Decode_MalformedDeclaredPayloadFailsClosedWithoutEchoingPayload(string payload)
    {
        var error = Assert.Throws<ProjectTreeProtocolException>(() =>
            ProjectTreeWorkerPayloadContract.Decode(
                SuccessfulWorker(payload, @"C:\Projects\Plant.ap21"),
                requestedSelector: null,
                requestedDepth: null));

        Assert.Equal(WorkerFailureCategories.ProtocolError, error.Category);
        Assert.DoesNotContain("secret-marker", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_MismatchedDepthFailsClosed()
    {
        var payload = Payload(startSelector: null, depth: 2, Node("PLC_1", ProjectTreeNodeTypes.Device));

        var error = Assert.Throws<ProjectTreeProtocolException>(() =>
            ProjectTreeWorkerPayloadContract.Decode(
                SuccessfulWorker(payload, @"C:\Projects\Plant.ap21"),
                requestedSelector: null,
                requestedDepth: 3));

        Assert.Equal(WorkerFailureCategories.ProtocolError, error.Category);
    }

    [Theory]
    [MemberData(nameof(NonEquivalentSelectors))]
    public void Decode_NonEquivalentCanonicalSelectorFailsClosed(
        IReadOnlyList<ProjectTreeSelectorSegment>? requested,
        IReadOnlyList<ProjectTreeSelectorSegment>? observed)
    {
        var payload = Payload(observed, depth: null, Node("PLC_1", ProjectTreeNodeTypes.Device));

        var error = Assert.Throws<ProjectTreeProtocolException>(() =>
            ProjectTreeWorkerPayloadContract.Decode(
                SuccessfulWorker(payload, @"C:\Projects\Plant.ap21"),
                requested,
                requestedDepth: null));

        Assert.Equal(WorkerFailureCategories.ProtocolError, error.Category);
    }

    [Fact]
    public void Decode_PreservesObservedSelectorCasing()
    {
        var requested = Selector(("Device", "plc_1"));
        var payload = Payload(Selector(("Device", "PLC_1")), depth: null, Node("PLC_1", "Device"));
        var observed = ProjectTreeWorkerPayloadContract.Decode(
            SuccessfulWorker(payload, @"C:\Projects\Plant.ap21"), requested, requestedDepth: null);

        Assert.Equal("PLC_1", Assert.Single(observed.CanonicalStartSelector!).Name);
    }

    [Fact]
    public void Decode_BlankResolvedProjectPathFailsClosed()
    {
        var payload = Payload(startSelector: null, depth: null, Node("PLC_1", ProjectTreeNodeTypes.Device));

        var error = Assert.Throws<ProjectTreeProtocolException>(() =>
            ProjectTreeWorkerPayloadContract.Decode(
                SuccessfulWorker(payload, "  "),
                requestedSelector: null,
                requestedDepth: null));

        Assert.Equal(WorkerFailureCategories.ProtocolError, error.Category);
    }

    [Fact]
    public void Decode_FailedWorkerResultNeverEntersTheSuccessDecoder()
    {
        var worker = WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "expected failure");

        var error = Assert.Throws<InvalidOperationException>(() =>
            ProjectTreeWorkerPayloadContract.Decode(worker, requestedSelector: null, requestedDepth: null));

        Assert.Equal("Only successful worker results may enter the project-tree payload decoder.", error.Message);
    }

    public static TheoryData<string> MalformedPayloads()
    {
        const string validNode =
            "{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{},\"children\":[]}";

        return new TheoryData<string>
        {
            "{\"startSelector\":null,\"depth\":null,\"roots\":[],\"secret-marker\":true}",
            "{\"startSelector\":null,\"depth\":null}",
            "{\"startSelector\":null,\"depth\":null,\"Roots\":[]}",
            "{\"startSelector\":null,\"depth\":null,\"roots\":null}",
            "{\"startSelector\":null,\"depth\":null,\"roots\":[null]}",
            "{\"startSelector\":null,\"depth\":null,\"roots\":[{\"name\":\"secret-marker\",\"nodeType\":\"device\",\"details\":{},\"children\":[]}]}",
            "{\"startSelector\":null,\"depth\":null,\"roots\":[{\"name\":\" \",\"nodeType\":\"Device\",\"details\":{\"marker\":\"secret-marker\"},\"children\":[]}]}",
            "{\"startSelector\":null,\"depth\":null,\"roots\":[{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{\"Path\":\"secret-marker\"},\"children\":[]}]}",
            "{\"startSelector\":null,\"depth\":null,\"roots\":[{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{\"pAtH\":\"secret-marker\"},\"children\":[]}]}",
            "{\"startSelector\":null,\"depth\":null,\"roots\":[{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{\"marker\":null},\"children\":[]}]}",
            "{\"startSelector\":null,\"depth\":null,\"roots\":[{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{},\"children\":null}]}",
            "{\"startSelector\":null,\"depth\":null,\"roots\":[{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{},\"children\":[null]}]}",
            $"{{\"startSelector\":null,\"depth\":null,\"roots\":[{{\"Name\":\"secret-marker\",\"nodeType\":\"Device\",\"details\":{{}},\"children\":[]}}]}}",
            $"{{\"startSelector\":null,\"depth\":null,\"roots\":[{{\"name\":\"secret-marker\",\"nodeType\":\"Device\",\"children\":[]}}]}}",
            $"{{\"startSelector\":[{{\"nodeType\":\"Device\",\"name\":\"PLC_1\",\"marker\":\"secret-marker\"}}],\"depth\":null,\"roots\":[{validNode}]}}",
            $"{{\"startSelector\":[{{\"NodeType\":\"Device\",\"name\":\"secret-marker\"}}],\"depth\":null,\"roots\":[{validNode}]}}",
            $"{{\"startSelector\":[{{\"nodeType\":\"Device\"}}],\"depth\":null,\"roots\":[{validNode}]}}",

            // Root members startSelector and depth are required raw-JSON keys (roots missing is
            // already covered two rows above).
            "{\"depth\":null,\"roots\":[]}",
            "{\"startSelector\":null,\"roots\":[]}",

            // A startSelector[] segment's nodeType/name are required to be strings, not merely
            // present (missing nodeType/name are already covered above via wrong casing).
            "{\"startSelector\":[{\"nodeType\":123,\"name\":\"PLC_1\"}],\"depth\":null,\"roots\":[]}",
            "{\"startSelector\":[{\"nodeType\":\"Device\",\"name\":123}],\"depth\":null,\"roots\":[]}",

            // A null element in startSelector[] (roots[]/children[] null elements are already
            // covered above).
            "{\"startSelector\":[null],\"depth\":null,\"roots\":[]}",

            // A node missing nodeType or children (name and details are already covered above).
            "{\"startSelector\":null,\"depth\":null,\"roots\":[{\"name\":\"PLC_1\",\"details\":{},\"children\":[]}]}",
            "{\"startSelector\":null,\"depth\":null,\"roots\":[{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{}}]}",

            // A roots[] element that is not an object at all (not just null).
            "{\"startSelector\":null,\"depth\":null,\"roots\":[\"not-an-object\"]}"
        };
    }

    public static TheoryData<IReadOnlyList<ProjectTreeSelectorSegment>?, IReadOnlyList<ProjectTreeSelectorSegment>?>
        NonEquivalentSelectors()
        => new()
        {
            { null, Selector((ProjectTreeNodeTypes.Device, "PLC_1")) },
            { Selector((ProjectTreeNodeTypes.Device, "PLC_1")), null },
            {
                Selector((ProjectTreeNodeTypes.Device, "PLC_1")),
                Selector((ProjectTreeNodeTypes.Device, "PLC_2"))
            },
            {
                Selector((ProjectTreeNodeTypes.Device, "PLC_1")),
                Selector((ProjectTreeNodeTypes.PlcSoftware, "PLC_1"))
            },
            {
                Selector((ProjectTreeNodeTypes.Device, "PLC_1")),
                Selector(
                    (ProjectTreeNodeTypes.Device, "PLC_1"),
                    (ProjectTreeNodeTypes.PlcSoftware, "PLC_1"))
            }
        };

    private static string Payload(
        IReadOnlyList<ProjectTreeSelectorSegment>? startSelector,
        int? depth,
        params ProjectTreeNode[] roots)
        => CanonicalJson.Serialize(new ProjectTreeBrowseResultInfo
        {
            StartSelector = startSelector?.ToList(),
            Depth = depth,
            Roots = roots.ToList()
        });

    private static ProjectTreeNode Node(string name, string nodeType, params ProjectTreeNode[] children)
        => new()
        {
            Name = name,
            NodeType = nodeType,
            Details = new Dictionary<string, string>(),
            Children = children.ToList()
        };

    private static IReadOnlyList<ProjectTreeSelectorSegment> Selector(
        params (string NodeType, string Name)[] segments)
        => segments.Select(segment => new ProjectTreeSelectorSegment
        {
            NodeType = segment.NodeType,
            Name = segment.Name
        }).ToList();

    private static WorkerCallResult SuccessfulWorker(
        string payload,
        string resolvedProjectPath,
        IReadOnlyList<string>? warnings = null)
        => WorkerCallResult.Ok(payload, warnings) with { ResolvedProjectPath = resolvedProjectPath };
}
