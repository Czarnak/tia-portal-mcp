using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
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

    [Fact]
    public void Decode_CopiesSkippedNodes()
    {
        var payload = CanonicalJson.Serialize(new ProjectTreeBrowseResultInfo
        {
            Roots = new List<ProjectTreeNode> { Node("PLC_1", ProjectTreeNodeTypes.Device) },
            Skipped = new List<ProjectTreeSkippedNodeInfo>
            {
                new()
                {
                    ParentPath = Selector((ProjectTreeNodeTypes.Device, "PLC_1")).ToList(),
                    NodeType = ProjectTreeNodeTypes.Block,
                    Reason = "block hidden"
                }
            }
        });

        var observed = ProjectTreeWorkerPayloadContract.Decode(
            SuccessfulWorker(payload, @"C:\Projects\Plant.ap21"), requestedSelector: null, requestedDepth: null);

        var skipped = Assert.Single(observed.Skipped);
        Assert.Equal(ProjectTreeNodeTypes.Block, skipped.NodeType);
        Assert.Equal("block hidden", skipped.Reason);
        Assert.Equal("PLC_1", Assert.Single(skipped.ParentPath).Name);
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

    // Reader-level rules: the shared worker-payload reader itself rejects these, before any typed rule.
    [Theory]
    [InlineData("startSelector")]
    [InlineData("depth")]
    [InlineData("roots")]
    [InlineData("roots[0].name")]
    [InlineData("roots[0].nodeType")]
    [InlineData("roots[0].details")]
    [InlineData("roots[0].children")]
    [InlineData("roots[0].children[0].children")]
    [InlineData("startSelector[0].nodeType")]
    [InlineData("startSelector[0].name")]
    public void Reader_RejectsAMissingMember(string path)
    {
        var mutated = CompleteBase();
        Assert.NotNull(CanonicalJson.DeserializeWorkerPayload<ProjectTreeBrowseResultInfo>(mutated.ToJsonString()));

        Remove(mutated, path);

        Assert.Throws<JsonException>(() =>
            CanonicalJson.DeserializeWorkerPayload<ProjectTreeBrowseResultInfo>(mutated.ToJsonString()));
    }

    [Fact]
    public void Decode_AcceptsTheCompleteBase()
    {
        var observed = ProjectTreeWorkerPayloadContract.Decode(
            SuccessfulWorker(CompleteBase().ToJsonString(), @"C:\Projects\Plant.ap21"),
            BaseSelector(),
            requestedDepth: 2);

        Assert.Equal("PLC_1", Assert.Single(observed.Roots).Name);
    }

    // Null-element rules: the reader does not check collection elements or dictionary values, so it
    // accepts each mutation and the typed validator must reject it.
    [Theory]
    [InlineData("roots[0]")]
    [InlineData("roots[0].children[0]")]
    [InlineData("roots[0].children")]
    [InlineData("roots[0].children[0].children")]
    [InlineData("roots[0].details.k")]
    [InlineData("startSelector[0]")]
    public void Decode_RejectsANullElementTheReaderAccepts(string path)
    {
        var mutated = CompleteBase();
        Set(mutated, path, null);

        AssertRejectedByTypedRules(mutated);
    }

    [Fact]
    public void Decode_RejectsAnEmptySelectorTheReaderAccepts()
    {
        var mutated = CompleteBase();
        mutated["startSelector"] = new JsonArray();

        AssertRejectedByTypedRules(mutated);
    }

    [Theory]
    [InlineData("roots[0].name", " ")]
    [InlineData("roots[0].nodeType", "device")]
    [InlineData("roots[0].children[0].name", "")]
    [InlineData("roots[0].children[0].nodeType", "Nope")]
    [InlineData("startSelector[0].nodeType", "device")]
    [InlineData("startSelector[0].name", " ")]
    [InlineData("startSelector[0].name", "PLC_2")]
    public void Decode_RejectsAStringThatViolatesATypedRule(string path, string value)
    {
        var mutated = CompleteBase();
        Set(mutated, path, JsonValue.Create(value));

        AssertRejectedByTypedRules(mutated);
    }

    [Fact]
    public void Decode_RejectsADepthThatDiffersFromTheRequest()
    {
        var mutated = CompleteBase();
        mutated["depth"] = 3;

        AssertRejectedByTypedRules(mutated);
    }

    [Theory]
    [InlineData("Path")]
    [InlineData("pAtH")]
    public void Decode_RejectsARemovedPathDetailTheReaderAccepts(string key)
    {
        var mutated = CompleteBase();
        mutated["roots"]![0]!["details"]![key] = "secret-marker";

        AssertRejectedByTypedRules(mutated);
    }

    [Fact]
    public void Decode_RejectsAnImpossibleSelectorTransitionTheReaderAccepts()
    {
        var mutated = CompleteBase();
        mutated["startSelector"]!.AsArray().Add(
            new JsonObject { ["nodeType"] = ProjectTreeNodeTypes.Device, ["name"] = "PLC_1" });

        AssertRejectedByTypedRules(
            mutated,
            Selector((ProjectTreeNodeTypes.Device, "PLC_1"), (ProjectTreeNodeTypes.Device, "PLC_1")));
    }

    // The base must decode, the mutation must pass the reader, and only then must the typed rules
    // reject it - otherwise an invalid fixture would make the rejection vacuous.
    private static void AssertRejectedByTypedRules(
        JsonObject mutated,
        IReadOnlyList<ProjectTreeSelectorSegment>? requested = null)
    {
        Assert.NotNull(ProjectTreeWorkerPayloadContract.Decode(
            SuccessfulWorker(CompleteBase().ToJsonString(), @"C:\Projects\Plant.ap21"),
            BaseSelector(),
            requestedDepth: 2));

        var json = mutated.ToJsonString();
        Assert.NotNull(CanonicalJson.DeserializeWorkerPayload<ProjectTreeBrowseResultInfo>(json));

        var error = Assert.Throws<ProjectTreeProtocolException>(() =>
            ProjectTreeWorkerPayloadContract.Decode(
                SuccessfulWorker(json, @"C:\Projects\Plant.ap21"),
                requested ?? BaseSelector(),
                requestedDepth: 2));

        Assert.Equal(WorkerFailureCategories.ProtocolError, error.Category);
        Assert.Equal(
            "The project-tree worker payload did not match its declared result contract and was rejected.",
            error.Message);
    }

    private static IReadOnlyList<ProjectTreeSelectorSegment> BaseSelector()
        => Selector((ProjectTreeNodeTypes.Device, "PLC_1"));

    private static JsonObject CompleteBase()
    {
        var root = Node(
            "PLC_1",
            ProjectTreeNodeTypes.Device,
            Node("Software", ProjectTreeNodeTypes.PlcSoftware));
        root.Details = new Dictionary<string, string> { ["k"] = "v" };
        return JsonNode.Parse(Payload(BaseSelector(), depth: 2, root))!.AsObject();
    }

    // Paths look like roots[0].children[0].name; the last step names the member or element changed.
    private static (JsonNode Parent, string? Member, int? Index) Resolve(JsonObject root, string path)
    {
        JsonNode current = root;
        var steps = path.Split('.');
        for (var i = 0; i < steps.Length; i++)
        {
            var bracket = steps[i].IndexOf('[');
            var name = bracket < 0 ? steps[i] : steps[i][..bracket];
            int? index = bracket < 0 ? null : int.Parse(steps[i][(bracket + 1)..^1]);
            if (i == steps.Length - 1)
            {
                return index is null ? (current, name, null) : (current[name]!, null, index);
            }

            current = current[name]!;
            if (index is not null)
            {
                current = current[index.Value]!;
            }
        }

        throw new InvalidOperationException("Empty path.");
    }

    private static void Set(JsonObject root, string path, JsonNode? value)
    {
        var (parent, member, index) = Resolve(root, path);
        if (member is not null)
        {
            parent.AsObject()[member] = value;
        }
        else
        {
            parent.AsArray()[index!.Value] = value;
        }
    }

    private static void Remove(JsonObject root, string path)
    {
        var (parent, member, _) = Resolve(root, path);
        parent.AsObject().Remove(member!);
    }

    public static TheoryData<string> MalformedPayloads()
    {
        const string validNode =
            "{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{},\"children\":[]}";

        return new TheoryData<string>
        {
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[],\"secret-marker\":true}",
            "{\"startSelector\":null,\"depth\":null}",
            "{\"startSelector\":null,\"depth\":null,\"Roots\":[]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":null}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[null]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[{\"name\":\"secret-marker\",\"nodeType\":\"device\",\"details\":{},\"children\":[]}]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[{\"name\":\" \",\"nodeType\":\"Device\",\"details\":{\"marker\":\"secret-marker\"},\"children\":[]}]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{\"Path\":\"secret-marker\"},\"children\":[]}]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{\"pAtH\":\"secret-marker\"},\"children\":[]}]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{\"marker\":null},\"children\":[]}]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{},\"children\":null}]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{},\"children\":[null]}]}",
            $"{{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[{{\"Name\":\"secret-marker\",\"nodeType\":\"Device\",\"details\":{{}},\"children\":[]}}]}}",
            $"{{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[{{\"name\":\"secret-marker\",\"nodeType\":\"Device\",\"children\":[]}}]}}",
            $"{{\"startSelector\":[{{\"nodeType\":\"Device\",\"name\":\"PLC_1\",\"marker\":\"secret-marker\"}}],\"depth\":null,\"skipped\":[],\"roots\":[{validNode}]}}",
            $"{{\"startSelector\":[{{\"NodeType\":\"Device\",\"name\":\"secret-marker\"}}],\"depth\":null,\"skipped\":[],\"roots\":[{validNode}]}}",
            $"{{\"startSelector\":[{{\"nodeType\":\"Device\"}}],\"depth\":null,\"skipped\":[],\"roots\":[{validNode}]}}",

            // Root members startSelector and depth are required; the worker-payload reader rejects
            // a root missing either (roots missing is already covered two rows above).
            "{\"depth\":null,\"skipped\":[],\"roots\":[]}",
            "{\"startSelector\":null,\"skipped\":[],\"roots\":[]}",

            // A startSelector[] segment's nodeType/name are required to be strings, not merely
            // present (missing nodeType/name are already covered above via wrong casing).
            "{\"startSelector\":[{\"nodeType\":123,\"name\":\"PLC_1\"}],\"depth\":null,\"skipped\":[],\"roots\":[]}",
            "{\"startSelector\":[{\"nodeType\":\"Device\",\"name\":123}],\"depth\":null,\"skipped\":[],\"roots\":[]}",

            // A null element in startSelector[] (roots[]/children[] null elements are already
            // covered above).
            "{\"startSelector\":[null],\"depth\":null,\"skipped\":[],\"roots\":[]}",

            // A node missing nodeType or children (name and details are already covered above).
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[{\"name\":\"PLC_1\",\"details\":{},\"children\":[]}]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[{\"name\":\"PLC_1\",\"nodeType\":\"Device\",\"details\":{}}]}",

            // A roots[] element that is not an object at all (not just null).
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[],\"roots\":[\"not-an-object\"]}",

            // skipped is required, non-null, and holds well-formed nodes.
            "{\"startSelector\":null,\"depth\":null,\"roots\":[]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":null,\"roots\":[]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[null],\"roots\":[]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[{\"parentPath\":[],\"nodeType\":\"Nope\",\"reason\":\"x\"}],\"roots\":[]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[{\"parentPath\":[{\"nodeType\":\"Device\",\"name\":\"A\"},{\"nodeType\":\"Device\",\"name\":\"B\"}],\"nodeType\":\"Block\",\"reason\":\"x\"}],\"roots\":[]}",
            "{\"startSelector\":null,\"depth\":null,\"skipped\":[{\"nodeType\":\"Block\",\"reason\":\"x\"}],\"roots\":[]}"
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
