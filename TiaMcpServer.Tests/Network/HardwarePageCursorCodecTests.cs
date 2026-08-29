using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class HardwarePageCursorCodecTests
{
    private static readonly byte[] TestKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] OtherKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static readonly string ProjectPath = ProjectPathNormalization.Canonicalize(@"C:\Projects\Sample.ap21")!;

    [Fact]
    public void EncodeDecode_WithInjectedKey_IsDeterministicAndRoundTrips()
    {
        var codec = new HardwarePageCursorCodec(TestKey);
        var state = State();

        var first = codec.Encode(state);
        var second = codec.Encode(state);
        var decoded = codec.Decode(first);

        Assert.Equal(first, second);
        Assert.DoesNotContain("=", first);
        Assert.Equal(1, first.Count(character => character == '.'));
        AssertState(state, decoded);
    }

    [Fact]
    public void Decode_RejectsAnyPayloadOrSignatureByteChange()
    {
        var codec = new HardwarePageCursorCodec(TestKey);
        var parts = codec.Encode(State()).Split('.');

        AssertCategory(WorkerFailureCategories.InvalidCursor, () =>
            codec.Decode($"{ChangeByte(parts[0])}.{parts[1]}"));
        AssertCategory(WorkerFailureCategories.InvalidCursor, () =>
            codec.Decode($"{parts[0]}.{ChangeByte(parts[1])}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%%%.AAAA")]
    [InlineData("one-part")]
    [InlineData("one.two.three")]
    [InlineData(".signature")]
    [InlineData("payload.")]
    public void Decode_RejectsMalformedBase64UrlAndEnvelopeParts(string cursor)
        => AssertCategory(
            WorkerFailureCategories.InvalidCursor,
            () => new HardwarePageCursorCodec(TestKey).Decode(cursor));

    [Fact]
    public void Decode_RejectsNonCanonicalBase64UrlPadBits()
    {
        var codec = new HardwarePageCursorCodec(TestKey);
        var parts = codec.Encode(State()).Split('.');
        var nonCanonicalSignature = ChangeUnusedPadBits(parts[1]);

        AssertCategory(
            WorkerFailureCategories.InvalidCursor,
            () => codec.Decode($"{parts[0]}.{nonCanonicalSignature}"));
    }

    [Fact]
    public void Decode_RejectsUnsupportedVersion()
    {
        var payload = ValidPayloadJson().Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal);

        AssertInvalidSignedPayload(payload);
    }

    [Fact]
    public void Decode_RejectsMissingMember()
    {
        var payload = ValidPayloadJson().Replace(",\"offset\":3", string.Empty, StringComparison.Ordinal);

        AssertInvalidSignedPayload(payload);
    }

    [Fact]
    public void Decode_RejectsExtraMember()
    {
        var payload = ValidPayloadJson();
        payload = payload[..^1] + ",\"extra\":true}";

        AssertInvalidSignedPayload(payload);
    }

    [Fact]
    public void Decode_RejectsDuplicateMember()
    {
        var payload = ValidPayloadJson();
        payload = payload[..^1] + ",\"version\":1}";

        AssertInvalidSignedPayload(payload);
    }

    [Fact]
    public void Decode_RejectsWrongJsonType()
    {
        var payload = ValidPayloadJson().Replace("\"offset\":3", "\"offset\":\"3\"", StringComparison.Ordinal);

        AssertInvalidSignedPayload(payload);
    }

    [Fact]
    public void Decode_RejectsTrailingJson()
        => AssertInvalidSignedPayload(ValidPayloadJson() + "{}");

    [Fact]
    public void Decode_RejectsBoundHostPathDifferentFromResolvedProject()
    {
        var state = State() with
        {
            HostBinding = new ProjectBindingCursorState(
                true,
                "binding-1",
                7,
                ProjectPathNormalization.Canonicalize(@"C:\Projects\Other.ap21")),
        };

        AssertInvalidSignedPayload(CanonicalJson.Serialize(state));
    }

    [Fact]
    public void Decode_RejectsCursorSignedByDifferentProcessKey()
    {
        var cursor = new HardwarePageCursorCodec(TestKey).Encode(State());

        AssertCategory(
            WorkerFailureCategories.InvalidCursor,
            () => new HardwarePageCursorCodec(OtherKey).Decode(cursor));
    }

    [Fact]
    public void Validate_QueryMismatch_ReturnsFilterMismatchBeforeWorkerCall()
    {
        var state = State();
        var request = Request(deviceName: "Different");

        var category = HardwarePageCursorValidator.Validate(state, request, BoundSnapshot());

        Assert.Equal(WorkerFailureCategories.CursorFilterMismatch, category);
    }

    [Fact]
    public void Validate_ExplicitProjectPathMismatch_ReturnsBindingMismatchBeforeWorkerCall()
    {
        var state = State();
        var request = Request(projectPath: @"C:\Projects\Other.ap21");

        var category = HardwarePageCursorValidator.Validate(state, request, BoundSnapshot());

        Assert.Equal(WorkerFailureCategories.CursorBindingMismatch, category);
    }

    [Fact]
    public void Validate_HostBindingSnapshotMismatch_ReturnsBindingMismatchBeforeWorkerCall()
    {
        var state = State();
        var changedBinding = BoundSnapshot(bindingId: "binding-2", revision: 8);

        var category = HardwarePageCursorValidator.Validate(state, Request(), changedBinding);

        Assert.Equal(WorkerFailureCategories.CursorBindingMismatch, category);
    }

    [Fact]
    public void Validate_PageSizeChange_DoesNotInvalidateCursor()
    {
        var state = State();

        var smallerPage = HardwarePageCursorValidator.Validate(state, Request(pageSize: 1), BoundSnapshot());
        var largerPage = HardwarePageCursorValidator.Validate(state, Request(pageSize: 200), BoundSnapshot());

        Assert.Null(smallerPage);
        Assert.Null(largerPage);
    }

    [Fact]
    public void Validate_OmittedProjectPathAndEquivalentNormalizedPath_AreAccepted()
    {
        var state = State();

        var omitted = HardwarePageCursorValidator.Validate(state, Request(), BoundSnapshot());
        var equivalent = HardwarePageCursorValidator.Validate(
            state,
            Request(projectPath: @"C:\Projects\.\Sample.ap21"),
            BoundSnapshot());

        Assert.Null(omitted);
        Assert.Null(equivalent);
    }

    [Fact]
    public void Validate_UnboundHostCursor_RemainsValidWhileHostIsUnbound()
    {
        var state = State(UnboundSnapshot());

        var category = HardwarePageCursorValidator.Validate(state, Request(), UnboundSnapshot("new-unbound-id", 9));

        Assert.Null(category);
        Assert.False(state.HostBinding.IsBound);
    }

    private static HardwarePageCursorState State(ProjectBindingSnapshot? binding = null)
        => new(
            Version: 1,
            ResolvedProjectPath: ProjectPath,
            SessionIdentity: new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-1",
                SessionGeneration = 4,
                PortalProcessId = 1234,
                ProjectPath = ProjectPath,
            },
            HostBinding: ProjectBindingCursorState.FromSnapshot(binding ?? BoundSnapshot()),
            QueryHash: HardwarePageEvidence.CreateQueryHash("PLC_1", "PLC_Main", true, true),
            OrderingVersion: 1,
            SnapshotHash: new string('a', 64),
            Offset: 3);

    private static NetworkOperationRequest Request(
        string? deviceName = "plc_1",
        string? projectPath = null,
        int? pageSize = 25)
        => new()
        {
            OperationId = "read-page",
            Operation = "read_hardware_config",
            ProjectPath = projectPath,
            DeviceName = deviceName,
            PlcName = "PLC_Main",
            IncludeIoDetails = true,
            IncludeTagMatches = true,
            PageSize = pageSize,
        };

    private static ProjectBindingSnapshot BoundSnapshot(
        string bindingId = "binding-1",
        long revision = 7,
        string? projectPath = null)
        => new(
            ProjectBindingSnapshot.VerifiedState,
            bindingId,
            revision,
            projectPath ?? ProjectPath,
            "worker-1",
            4,
            1234,
            null);

    private static ProjectBindingSnapshot UnboundSnapshot(string bindingId = "unbound-id", long revision = 0)
        => new(
            ProjectBindingSnapshot.UnboundState,
            bindingId,
            revision,
            null,
            null,
            null,
            null,
            null);

    private static string ValidPayloadJson()
    {
        var cursor = new HardwarePageCursorCodec(TestKey).Encode(State());
        return Encoding.UTF8.GetString(DecodeBase64Url(cursor.Split('.')[0]));
    }

    private static void AssertInvalidSignedPayload(string payload)
        => AssertCategory(
            WorkerFailureCategories.InvalidCursor,
            () => new HardwarePageCursorCodec(TestKey).Decode(Sign(payload, TestKey)));

    private static string Sign(string payload, byte[] key)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(key);
        return $"{EncodeBase64Url(payloadBytes)}.{EncodeBase64Url(hmac.ComputeHash(payloadBytes))}";
    }

    private static string ChangeByte(string value)
        => (value[0] == 'A' ? "B" : "A") + value[1..];

    private static string ChangeUnusedPadBits(string value)
    {
        var replacement = value[^1] switch
        {
            'A' => 'B',
            'Q' => 'R',
            'g' => 'h',
            'w' => 'x',
            _ => throw new InvalidOperationException("A 32-byte value must end in canonical two-bit base64 data."),
        };
        return value[..^1] + replacement;
    }

    private static string EncodeBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }

    private static void AssertCategory(string expected, Action action)
    {
        var exception = Assert.Throws<HardwarePageCursorException>(action);
        Assert.Equal(expected, exception.Category);
        Assert.DoesNotContain("worker-1", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ProjectPath, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertState(HardwarePageCursorState expected, HardwarePageCursorState actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.ResolvedProjectPath, actual.ResolvedProjectPath);
        Assert.Equal(expected.SessionIdentity.WorkerSessionId, actual.SessionIdentity.WorkerSessionId);
        Assert.Equal(expected.SessionIdentity.SessionGeneration, actual.SessionIdentity.SessionGeneration);
        Assert.Equal(expected.SessionIdentity.PortalProcessId, actual.SessionIdentity.PortalProcessId);
        Assert.Equal(expected.SessionIdentity.ProjectPath, actual.SessionIdentity.ProjectPath);
        Assert.Equal(expected.HostBinding, actual.HostBinding);
        Assert.Equal(expected.QueryHash, actual.QueryHash);
        Assert.Equal(expected.OrderingVersion, actual.OrderingVersion);
        Assert.Equal(expected.SnapshotHash, actual.SnapshotHash);
        Assert.Equal(expected.Offset, actual.Offset);
    }
}
