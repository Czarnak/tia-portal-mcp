using System;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

/// <summary>
/// Locks the strict, vendor-free JSON validation boundary for the internal
/// <c>probe_vci_read_contract</c> worker operation. <see cref="VciProbeJsonBoundary.Validate"/> is
/// pure System.Text.Json — no Siemens dependency — and must never throw for malformed input.
///
/// <para>
/// This is a Phase 1 Task 1 test — it exercises only the JSON boundary, never Siemens Openness. No
/// worker dispatch, no live TIA Portal.
/// </para>
/// </summary>
public class VciProbeJsonBoundaryTests
{
    private const string ValidProbe =
        "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
        + "\"caseId\":\"R-SVC\",\"caseInstanceId\":\"i\"}";

    private static string Wrap(string vciProbeJson, string method = "probe_vci_read_contract")
        => "{\"method\":\"" + method + "\",\"projectPath\":\"C:\\\\P.ap21\",\"vciProbe\":" + vciProbeJson + "}";

    [Fact]
    public void Validate_AcceptsAWellFormedProbeRequest()
    {
        Assert.Null(VciProbeJsonBoundary.Validate(Wrap(ValidProbe)));
    }

    [Fact]
    public void Validate_ReturnsNullForNonVciMethods()
    {
        var json = "{\"method\":\"get_block_content\",\"blockPath\":\"Main\",\"confirm\":false}";

        Assert.Null(VciProbeJsonBoundary.Validate(json));
    }

    [Fact]
    public void Validate_ReturnsNullWhenMethodCaseDoesNotMatchAtAllAndFieldsAreForeign()
    {
        // A request for a totally different operation that happens to carry write flags must not
        // be touched by this boundary — that is exactly the "existing operations keep their
        // permissive JSON behavior" guarantee.
        var json = "{\"method\":\"update_tag\",\"confirm\":true,\"allowTiaConfirmations\":true}";

        Assert.Null(VciProbeJsonBoundary.Validate(json));
    }

    [Theory]
    [InlineData("PROBE_VCI_READ_CONTRACT")]
    [InlineData("Probe_Vci_Read_Contract")]
    public void Validate_MatchesMethodCaseInsensitively(string method)
    {
        Assert.Null(VciProbeJsonBoundary.Validate(Wrap(ValidProbe, method)));
    }

    [Fact]
    public void Validate_AlternateCaseMethodPropertyCannotBypassStrictEnvelope()
    {
        var json = "{\"Method\":\"probe_vci_read_contract\",\"projectPath\":\"C:\\\\P.ap21\","
            + "\"vciProbe\":" + ValidProbe + "}";

        Assert.Equal("Unknown root field 'Method'.", VciProbeJsonBoundary.Validate(json));
    }

    [Theory]
    [InlineData("\"extraRoot\":1", "extraRoot")]
    [InlineData("\"confirm\":false", "confirm")]
    [InlineData("\"allowTiaConfirmations\":false", "allowTiaConfirmations")]
    public void Validate_AlternateCaseMethodPropertyCannotBypassUnknownOrWriteRootFields(
        string rootField,
        string expectedField)
    {
        var json = "{" + rootField + ",\"Method\":\"probe_vci_read_contract\","
            + "\"vciProbe\":" + ValidProbe + "}";

        var error = VciProbeJsonBoundary.Validate(json);

        Assert.NotNull(error);
        Assert.Contains(expectedField, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_DuplicateMethodCasingCannotHideProbeDispatch()
    {
        var json = "{\"method\":\"get_block_content\",\"Method\":\"probe_vci_read_contract\","
            + "\"vciProbe\":" + ValidProbe + "}";

        var error = VciProbeJsonBoundary.Validate(json);

        Assert.NotNull(error);
        Assert.Contains("Duplicate root field 'Method'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AlternateCaseMethodPropertyStillPassesThroughForNonVciMethods()
    {
        var json = "{\"Method\":\"get_block_content\",\"blockPath\":\"Main\",\"confirm\":false}";

        Assert.Null(VciProbeJsonBoundary.Validate(json));
    }

    [Fact]
    public void Validate_NeverThrowsOnUnparsableJson()
    {
        var exception = Record.Exception(() => VciProbeJsonBoundary.Validate("{ not json"));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_ReturnsNullForUnparsableJson()
    {
        Assert.Null(VciProbeJsonBoundary.Validate("{ not json"));
    }

    [Theory]
    [InlineData("{\"method\":\"probe_vci_read_contract\",\"projectPath\":\"C:\\\\P.ap21\",\"vciProbe\":[]}", "vciProbe must be an object")]
    [InlineData("{\"method\":\"probe_vci_read_contract\",\"projectPath\":\"C:\\\\P.ap21\",\"vciProbe\":\"x\"}", "vciProbe must be an object")]
    [InlineData("{\"method\":\"probe_vci_read_contract\",\"projectPath\":\"C:\\\\P.ap21\",\"vciProbe\":1}", "vciProbe must be an object")]
    public void Validate_RejectsNonObjectVciProbe(string json, string expected)
    {
        var error = VciProbeJsonBoundary.Validate(json);

        Assert.NotNull(error);
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsMissingVciProbe()
    {
        var json = "{\"method\":\"probe_vci_read_contract\",\"projectPath\":\"C:\\\\P.ap21\"}";

        var error = VciProbeJsonBoundary.Validate(json);

        Assert.NotNull(error);
        Assert.Contains("vciProbe", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsUnknownRootField()
    {
        var json = "{\"method\":\"probe_vci_read_contract\",\"vciProbe\":" + ValidProbe + ",\"extraRoot\":1}";

        var error = VciProbeJsonBoundary.Validate(json);

        Assert.NotNull(error);
        Assert.Contains("extraRoot", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("allowTiaConfirmations")]
    public void Validate_RejectsWriteFlagsEvenWhenFalse(string flagName)
    {
        var json = "{\"method\":\"probe_vci_read_contract\",\"vciProbe\":" + ValidProbe
            + ",\"" + flagName + "\":false}";

        var error = VciProbeJsonBoundary.Validate(json);

        Assert.NotNull(error);
        Assert.Contains(flagName, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsUnknownVciProbeField()
    {
        var json = "{\"method\":\"probe_vci_read_contract\",\"projectPath\":\"C:\\\\P.ap21\","
            + "\"vciProbe\":{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"R-SVC\",\"caseInstanceId\":\"i\",\"extra\":1}}";

        var error = VciProbeJsonBoundary.Validate(json);

        Assert.NotNull(error);
        Assert.Contains("Unknown vciProbe field 'extra'", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("runId")]
    [InlineData("sessionId")]
    [InlineData("caseId")]
    [InlineData("caseInstanceId")]
    [InlineData("schemaVersion")]
    public void Validate_RejectsWrongTypeForRequiredScalar(string field)
    {
        var json = Wrap(BuildProbeWithNumericField(field));

        var error = VciProbeJsonBoundary.Validate(json);

        Assert.NotNull(error);
        Assert.Contains(field, error, StringComparison.Ordinal);
    }

    private static string BuildProbeWithNumericField(string numericField)
    {
        string Field(string name, string defaultValue)
            => name == numericField ? $"\"{name}\":123" : $"\"{name}\":\"{defaultValue}\"";

        return "{" + string.Join(",",
            Field("schemaVersion", "vci-read-probe/v1"),
            Field("runId", "r"),
            Field("sessionId", "s"),
            Field("caseId", "R-SVC"),
            Field("caseInstanceId", "i")) + "}";
    }

    [Theory]
    [InlineData("runId")]
    [InlineData("sessionId")]
    [InlineData("caseId")]
    [InlineData("caseInstanceId")]
    [InlineData("schemaVersion")]
    public void Validate_RejectsExplicitJsonNullForRequiredScalar(string field)
    {
        var json = Wrap(BuildProbeWithNullField(field));

        var error = VciProbeJsonBoundary.Validate(json);

        Assert.NotNull(error);
        Assert.Contains(field, error, StringComparison.Ordinal);
        Assert.Contains("null", error, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildProbeWithNullField(string nullField)
    {
        string Field(string name, string defaultValue)
            => name == nullField ? $"\"{name}\":null" : $"\"{name}\":\"{defaultValue}\"";

        return "{" + string.Join(",",
            Field("schemaVersion", "vci-read-probe/v1"),
            Field("runId", "r"),
            Field("sessionId", "s"),
            Field("caseId", "R-SVC"),
            Field("caseInstanceId", "i")) + "}";
    }

    [Fact]
    public void Validate_AllowsRequiredFieldOmittedButFlagsItSemantically()
    {
        // Omitting a required scalar is a structurally valid JSON shape (unlike explicit null) —
        // it surfaces as a semantic rejection (blank field) via VciReadProbeContract, not a JSON
        // boundary type error.
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"sessionId\":\"s\","
            + "\"caseId\":\"R-SVC\",\"caseInstanceId\":\"i\"}"; // runId omitted

        var error = VciProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains("runId", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("maxGroupDepth")]
    [InlineData("maxGroups")]
    [InlineData("maxWorkspaces")]
    [InlineData("maxMappings")]
    [InlineData("maxEngineeringObjects")]
    [InlineData("maxCollectionItems")]
    public void Validate_RejectsNonIntegerBudget(string field)
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"R-SVC\",\"caseInstanceId\":\"i\",\"" + field + "\":\"not-a-number\"}";

        var error = VciProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains(field, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("maxGroupDepth")]
    [InlineData("maxGroups")]
    public void Validate_RejectsExplicitJsonNullForBudget(string field)
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"R-SVC\",\"caseInstanceId\":\"i\",\"" + field + "\":null}";

        var error = VciProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains(field, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsUnknownCaseId()
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"NOT-A-REAL-CASE\",\"caseInstanceId\":\"i\"}";

        var error = VciProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains("caseId", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsDuplicateFieldsDifferingOnlyByCase()
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"RunId\":\"r2\","
            + "\"sessionId\":\"s\",\"caseId\":\"R-SVC\",\"caseInstanceId\":\"i\"}";

        var error = VciProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains("Duplicate", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsDuplicateRootFieldsDifferingOnlyByCase()
    {
        var json = "{\"method\":\"probe_vci_read_contract\",\"vciProbe\":" + ValidProbe
            + ",\"ProjectPath\":\"C:\\\\Other.ap21\",\"projectPath\":\"C:\\\\P.ap21\"}";

        var error = VciProbeJsonBoundary.Validate(json);

        Assert.NotNull(error);
        Assert.Contains("Duplicate", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsWrongTypeForWorkspaceSelector()
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"R-FMT\",\"caseInstanceId\":\"i\",\"workspace\":\"not-an-object\"}";

        var error = VciProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains("workspace", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsUnknownWorkspaceSelectorField()
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"R-FMT\",\"caseInstanceId\":\"i\","
            + "\"workspace\":{\"workspaceName\":\"WS1\",\"bogus\":1}}";

        var error = VciProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains("bogus", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsWrongTypeForEngineeringObjectSelector()
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"R-FMT\",\"caseInstanceId\":\"i\",\"engineeringObject\":[]}";

        var error = VciProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains("engineeringObject", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsUnknownEngineeringObjectSelectorField()
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"R-FMT\",\"caseInstanceId\":\"i\","
            + "\"engineeringObject\":{\"stableIdentifier\":\"id\",\"bogus\":1}}";

        var error = VciProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains("bogus", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AllowsExplicitNullWorkspaceAndEngineeringObjectSelectors()
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"N-FMT-NULL\",\"caseInstanceId\":\"i\",\"workspace\":null,\"engineeringObject\":null}";

        Assert.Null(VciProbeJsonBoundary.Validate(Wrap(probe)));
    }

    [Fact]
    public void Validate_RejectsMissingWorkspaceSelectorForRFmt()
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"R-FMT\",\"caseInstanceId\":\"i\","
            + "\"engineeringObject\":{\"stableIdentifier\":\"id\"}}";

        var error = VciProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains("workspace", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AcceptsRFmtCaseWithBothSelectorsPresent()
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"R-FMT\",\"caseInstanceId\":\"i\","
            + "\"workspace\":{\"workspaceName\":\"WS1\",\"groupPath\":[{\"index\":0,\"name\":\"Root\"}]},"
            + "\"engineeringObject\":{\"stableIdentifier\":\"id\"}}";

        Assert.Null(VciProbeJsonBoundary.Validate(Wrap(probe)));
    }

    [Fact]
    public void Validate_AcceptsCompleteRFmtCaseWithSameNameOrdinal()
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"R-FMT\",\"caseInstanceId\":\"i\","
            + "\"workspace\":{\"workspaceName\":\"WS1\","
            + "\"groupPath\":[{\"index\":2,\"name\":\"Programs\",\"sameNameOrdinal\":1}]},"
            + "\"engineeringObject\":{\"stableIdentifier\":\"id\"}}";

        Assert.Null(VciProbeJsonBoundary.Validate(Wrap(probe)));
    }

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public void Validate_RejectsSameNameOrdinalOutsideJsonInt32(string sameNameOrdinalJson)
    {
        var probe = "{\"schemaVersion\":\"vci-read-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\","
            + "\"caseId\":\"R-FMT\",\"caseInstanceId\":\"i\","
            + "\"workspace\":{\"workspaceName\":\"WS1\","
            + "\"groupPath\":[{\"index\":2,\"name\":\"Programs\",\"sameNameOrdinal\":"
            + sameNameOrdinalJson + "}]},"
            + "\"engineeringObject\":{\"stableIdentifier\":\"id\"}}";

        Assert.Equal(
            "'vciProbe.workspace.groupPath[].sameNameOrdinal' must be an integer.",
            VciProbeJsonBoundary.Validate(Wrap(probe)));
    }

    [Fact]
    public void Validate_RejectsWrongTypeForProjectPath()
    {
        var json = "{\"method\":\"probe_vci_read_contract\",\"projectPath\":123,\"vciProbe\":" + ValidProbe + "}";

        var error = VciProbeJsonBoundary.Validate(json);

        Assert.NotNull(error);
        Assert.Contains("projectPath", error, StringComparison.Ordinal);
    }
}
