using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using Xunit;

namespace TiaMcpServer.Tests.Json;

/// <summary>
/// Contract tests for the worker-payload reader: the same strict pipeline as
/// <see cref="CanonicalJson.Deserialize{T}"/>/<see cref="CanonicalJson.Normalize{T}"/>, plus every
/// settable member required unless declared conditional with
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/>, and a refusal of any root that still omits
/// null members on the wire (<see cref="WorkerJson.OmitsNullMembers"/>).
/// </summary>
public class CanonicalJsonWorkerPayloadTests
{
    private sealed class NestedDto
    {
        public string RequiredValue { get; set; } = string.Empty;
    }

    private sealed class TopLevelDto
    {
        public string RequiredString { get; set; } = string.Empty;

        public string? NullableString { get; set; }

        public List<string> RequiredList { get; set; } = new();

        public NestedDto Nested { get; set; } = new();

        public List<NestedDto> NestedList { get; set; } = new();

        /// <summary>Present only for scenarios that opt in; absent otherwise.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ConditionalMember { get; set; }
    }

    private sealed class ArrayElementDto
    {
        public string TypeName { get; set; } = string.Empty;

        public string TypeIdentifier { get; set; } = string.Empty;
    }

    [LegacyNullOmission(LegacyNullOmissionReason.ToolMigration)]
    private sealed class RefusedDto
    {
        public string Value { get; set; } = string.Empty;
    }

    [LegacyNullOmission(LegacyNullOmissionReason.ToolMigration)]
    private sealed class RefusedArrayElementDto
    {
        public string Value { get; set; } = string.Empty;
    }

    private const string ValidTopLevelJson =
        """{"requiredString":"a","nullableString":null,"requiredList":["x"],"nested":{"requiredValue":"n"},"nestedList":[{"requiredValue":"m"}]}""";

    // --- Rejected -----------------------------------------------------------------------------

    [Fact]
    public void DeserializeWorkerPayload_RejectsMissingMemberAtTopLevel()
    {
        // ValidTopLevelJson with "requiredString" removed.
        const string json =
            """{"nullableString":null,"requiredList":["x"],"nested":{"requiredValue":"n"},"nestedList":[{"requiredValue":"m"}]}""";

        Assert.Throws<JsonException>(() => CanonicalJson.DeserializeWorkerPayload<TopLevelDto>(json));
    }

    [Fact]
    public void DeserializeWorkerPayload_RejectsMissingMemberInNestedObject()
    {
        // ValidTopLevelJson with "nested.requiredValue" removed.
        const string json =
            """{"requiredString":"a","nullableString":null,"requiredList":["x"],"nested":{},"nestedList":[{"requiredValue":"m"}]}""";

        Assert.Throws<JsonException>(() => CanonicalJson.DeserializeWorkerPayload<TopLevelDto>(json));
    }

    [Fact]
    public void DeserializeWorkerPayload_RejectsMissingMemberInsideArrayElement()
    {
        // ValidTopLevelJson with "nestedList[0].requiredValue" removed.
        const string json =
            """{"requiredString":"a","nullableString":null,"requiredList":["x"],"nested":{"requiredValue":"n"},"nestedList":[{}]}""";

        Assert.Throws<JsonException>(() => CanonicalJson.DeserializeWorkerPayload<TopLevelDto>(json));
    }

    [Fact]
    public void DeserializeWorkerPayload_RejectsExplicitNullInNonNullableString()
    {
        // ValidTopLevelJson with "requiredString" set to null.
        const string json =
            """{"requiredString":null,"nullableString":null,"requiredList":["x"],"nested":{"requiredValue":"n"},"nestedList":[{"requiredValue":"m"}]}""";

        Assert.Throws<JsonException>(() => CanonicalJson.DeserializeWorkerPayload<TopLevelDto>(json));
    }

    [Fact]
    public void DeserializeWorkerPayload_RejectsExplicitNullInNonNullableList()
    {
        // ValidTopLevelJson with "requiredList" set to null.
        const string json =
            """{"requiredString":"a","nullableString":null,"requiredList":null,"nested":{"requiredValue":"n"},"nestedList":[{"requiredValue":"m"}]}""";

        Assert.Throws<JsonException>(() => CanonicalJson.DeserializeWorkerPayload<TopLevelDto>(json));
    }

    [Fact]
    public void DeserializeWorkerPayload_RejectsUnknownMember()
    {
        // ValidTopLevelJson with an extra "unknown" member.
        const string json =
            """{"requiredString":"a","nullableString":null,"requiredList":["x"],"nested":{"requiredValue":"n"},"nestedList":[{"requiredValue":"m"}],"unknown":1}""";

        Assert.Throws<JsonException>(() => CanonicalJson.DeserializeWorkerPayload<TopLevelDto>(json));
    }

    [Fact]
    public void DeserializeWorkerPayload_RejectsDuplicateProperty()
    {
        const string json =
            """{"requiredString":"a","requiredString":"b","nullableString":null,"requiredList":["x"],"nested":{"requiredValue":"n"},"nestedList":[{"requiredValue":"m"}]}""";

        Assert.Throws<JsonException>(() => CanonicalJson.DeserializeWorkerPayload<TopLevelDto>(json));
    }

    [Theory]

    // Each case is HardwarePaginationInfo's valid fixture with exactly one non-conditional member
    // removed. NextCursor is conditional and is covered separately.
    [InlineData("""{"totalSubnets":3,"returnedDevices":2,"returnedSubnets":1}""")]
    [InlineData("""{"totalDevices":10,"returnedDevices":2,"returnedSubnets":1}""")]
    [InlineData("""{"totalDevices":10,"totalSubnets":3,"returnedSubnets":1}""")]
    [InlineData("""{"totalDevices":10,"totalSubnets":3,"returnedDevices":2}""")]
    public void DeserializeWorkerPayload_RejectsHardwarePaginationInfoMissingAnyOtherMember(string json)
    {
        Assert.Throws<JsonException>(
            () => CanonicalJson.DeserializeWorkerPayload<HardwarePaginationInfo>(json));
    }

    [Fact]
    public void DeserializeWorkerPayload_RejectsArrayRootElementMissingAMember()
    {
        // Replaces a brief-named CatalogEntryInfo[] fixture: CatalogEntryInfo still carries
        // [LegacyNullOmission(RequiredMemberEnforcement)] until Task 3 removes it, so
        // DeserializeWorkerPayload<CatalogEntryInfo[]> would refuse this input (and any input)
        // before ever reaching element validation. This test-local, unmarked element type proves
        // the same "array root -> per-element required members" behavior without that conflict.
        // The real CatalogEntryInfo[] pair moves to Task 3.
        const string json = """[{"typeName":"A","typeIdentifier":"1"},{"typeName":"B"}]""";

        Assert.Throws<JsonException>(
            () => CanonicalJson.DeserializeWorkerPayload<ArrayElementDto[]>(json));
    }

    [Fact]
    public void DeserializeWorkerPayload_RejectsCatalogEntryInfoArrayRootElementMissingAMember()
    {
        // Carry-over from Task 2: CatalogEntryInfo no longer carries
        // [LegacyNullOmission(RequiredMemberEnforcement)] (removed in Task 3), so the real
        // CatalogEntryInfo[] root can now be exercised directly instead of the unmarked
        // ArrayElementDto stand-in above. Second element is missing "description".
        const string json =
            """[{"typeName":"A","articleNumber":"6ES7 123-4BB00","version":"V1.0","typeIdentifier":"1","typeIdentifierNormalized":"1-normalized","catalogPath":"TypeCatalog/A","description":"Desc A"},{"typeName":"B","articleNumber":null,"version":null,"typeIdentifier":"2","typeIdentifierNormalized":null,"catalogPath":null}]""";

        Assert.Throws<JsonException>(
            () => CanonicalJson.DeserializeWorkerPayload<CatalogEntryInfo[]>(json));
    }

    // --- Accepted -------------------------------------------------------------------------------

    [Fact]
    public void DeserializeWorkerPayload_AcceptsNullInNullableMember()
    {
        var value = CanonicalJson.DeserializeWorkerPayload<TopLevelDto>(ValidTopLevelJson);

        Assert.Null(value.NullableString);
    }

    [Fact]
    public void DeserializeWorkerPayload_AcceptsConditionalMemberAbsent()
    {
        var value = CanonicalJson.DeserializeWorkerPayload<TopLevelDto>(ValidTopLevelJson);

        Assert.Null(value.ConditionalMember);
    }

    [Fact]
    public void DeserializeWorkerPayload_AcceptsConditionalMemberPresent()
    {
        const string json =
            """{"requiredString":"a","nullableString":null,"requiredList":["x"],"nested":{"requiredValue":"n"},"nestedList":[{"requiredValue":"m"}],"conditionalMember":"present"}""";

        var value = CanonicalJson.DeserializeWorkerPayload<TopLevelDto>(json);

        Assert.Equal("present", value.ConditionalMember);
    }

    [Fact]
    public void DeserializeWorkerPayload_AcceptsNetworkAttributeValueInfoWithNullValue()
    {
        // [JsonIgnore(Condition = Never)] object? Value is required (has a setter, not
        // WhenWritingNull) but nullable, so an explicit "value": null must be accepted.
        const string json = """{"kind":"null","value":null,"typeName":null}""";

        var value = CanonicalJson.DeserializeWorkerPayload<NetworkAttributeValueInfo>(json);

        Assert.Equal("null", value.Kind);
        Assert.Null(value.Value);
    }

    [Fact]
    public void DeserializeWorkerPayload_AcceptsHardwarePaginationInfoWithNextCursorAbsent()
    {
        const string json = """{"totalDevices":10,"totalSubnets":3,"returnedDevices":2,"returnedSubnets":1}""";

        var value = CanonicalJson.DeserializeWorkerPayload<HardwarePaginationInfo>(json);

        Assert.Null(value.NextCursor);
    }

    [Fact]
    public void DeserializeWorkerPayload_AcceptsArrayRootOfUnmarkedElementType()
    {
        // Replaces a brief-named CatalogEntryInfo[] fixture; see the rejection test above for why.
        const string json = """[{"typeName":"A","typeIdentifier":"1"},{"typeName":"B","typeIdentifier":"2"}]""";

        var value = CanonicalJson.DeserializeWorkerPayload<ArrayElementDto[]>(json);

        Assert.Equal(2, value.Length);
        Assert.Equal("A", value[0].TypeName);
        Assert.Equal("2", value[1].TypeIdentifier);
    }

    [Fact]
    public void DeserializeWorkerPayload_AcceptsCatalogEntryInfoArrayRoot()
    {
        // Carry-over from Task 2: exercises the reader on the real CatalogEntryInfo[] root now
        // that Task 3 removed its [LegacyNullOmission(RequiredMemberEnforcement)] marker.
        const string json =
            """[{"typeName":"A","articleNumber":"6ES7 123-4BB00","version":"V1.0","typeIdentifier":"1","typeIdentifierNormalized":"1-normalized","catalogPath":"TypeCatalog/A","description":"Desc A"},{"typeName":"B","articleNumber":null,"version":null,"typeIdentifier":"2","typeIdentifierNormalized":null,"catalogPath":null,"description":null}]""";

        var value = CanonicalJson.DeserializeWorkerPayload<CatalogEntryInfo[]>(json);

        Assert.Equal(2, value.Length);
        Assert.Equal("A", value[0].TypeName);
        Assert.Null(value[1].ArticleNumber);
    }

    // --- Refused --------------------------------------------------------------------------------

    [Fact]
    public void DeserializeWorkerPayload_AndNormalizeWorkerPayload_RefuseLegacyNullOmittingPayload()
    {
        const string json = """{"value":"a"}""";

        Assert.Throws<InvalidOperationException>(
            () => CanonicalJson.DeserializeWorkerPayload<RefusedDto>(json));
        Assert.Throws<InvalidOperationException>(
            () => CanonicalJson.NormalizeWorkerPayload<RefusedDto>(json));
    }

    [Fact]
    public void DeserializeWorkerPayload_AndNormalizeWorkerPayload_RefuseArrayRootOfLegacyNullOmittingElement()
    {
        // Pins that the refusal unwraps an array root the same way WorkerJson.OmitsNullMembers
        // does, so a marked element type refuses the whole array root, not just its elements.
        const string json = """[{"value":"a"}]""";

        Assert.Throws<InvalidOperationException>(
            () => CanonicalJson.DeserializeWorkerPayload<RefusedArrayElementDto[]>(json));
        Assert.Throws<InvalidOperationException>(
            () => CanonicalJson.NormalizeWorkerPayload<RefusedArrayElementDto[]>(json));
    }

    // --- Normalize output -------------------------------------------------------------------

    [Fact]
    public void NormalizeWorkerPayload_ReturnsSameCanonicalTextAsNormalize()
    {
        var viaWorkerPayload = CanonicalJson.NormalizeWorkerPayload<TopLevelDto>(ValidTopLevelJson);
        var viaNormalize = CanonicalJson.Normalize<TopLevelDto>(ValidTopLevelJson);

        Assert.Equal(viaNormalize.Text, viaWorkerPayload.Text);
    }
}
