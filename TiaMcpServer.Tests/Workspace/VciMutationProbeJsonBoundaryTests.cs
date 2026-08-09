using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

public class VciMutationProbeJsonBoundaryTests
{
    private const string ValidProbe =
        "{\"schemaVersion\":\"vci-mutation-probe/v1\",\"runId\":\"r\",\"sessionId\":\"s\"," +
        "\"scenarioId\":\"scenario\",\"caseId\":\"P-INVENTORY\",\"caseInstanceId\":\"i\"," +
        "\"mode\":\"Inventory\",\"workspaceRoot\":\"C:\\\\vci-root\"," +
        "\"engineeringObject\":{\"structuralPath\":[]},\"fileFormat\":\"SimaticML\"}";

    [Fact]
    public void Validate_AcceptsTheMinimalInventoryEnvelope()
        => Assert.Null(VciMutationProbeJsonBoundary.Validate(Wrap(ValidProbe)));

    [Fact]
    public void Validate_TargetsTheOperationCaseInsensitively()
        => Assert.Null(VciMutationProbeJsonBoundary.Validate(
            Wrap(ValidProbe).Replace("probe_vci_mutation_contract", "PROBE_VCI_MUTATION_CONTRACT", StringComparison.Ordinal)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    public void Validate_LeavesMalformedOrNonObjectJsonToNormalDeserialization(string? json)
        => Assert.Null(VciMutationProbeJsonBoundary.Validate(json));

    [Fact]
    public void Validate_DoesNotAffectOtherWorkerMethods()
    {
        var json = "{\"method\":\"get_project_status\",\"vciMutationProbe\":123,\"confirm\":true}";

        Assert.Null(VciMutationProbeJsonBoundary.Validate(json));
    }

    [Fact]
    public void Validate_RejectsMissingOrNonObjectProbe()
    {
        Assert.Equal(
            "'vciMutationProbe' is required.",
            VciMutationProbeJsonBoundary.Validate("{\"method\":\"probe_vci_mutation_contract\"}"));
        Assert.Equal(
            "vciMutationProbe must be an object; found number.",
            VciMutationProbeJsonBoundary.Validate(
                "{\"method\":\"probe_vci_mutation_contract\",\"vciMutationProbe\":1}"));
    }

    [Theory]
    [InlineData("\"confirm\":true")]
    [InlineData("\"allowTiaConfirmations\":true")]
    [InlineData("\"unknownRoot\":1")]
    public void Validate_RejectsUnknownRootFieldsIncludingWriteFlags(string field)
    {
        var json = "{\"method\":\"probe_vci_mutation_contract\"," + field + ",\"vciMutationProbe\":" + ValidProbe + "}";

        var error = VciMutationProbeJsonBoundary.Validate(json);

        Assert.NotNull(error);
        Assert.Contains("root", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsUnknownProbeField()
    {
        var probe = ValidProbe.Insert(ValidProbe.Length - 1, ",\"methodName\":\"Delete\"");

        var error = VciMutationProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains("methodName", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"method\":\"probe_vci_mutation_contract\",\"Method\":\"probe_vci_mutation_contract\",\"vciMutationProbe\":")]
    [InlineData("{\"method\":\"probe_vci_mutation_contract\",\"vciMutationProbe\":")]
    public void Validate_RejectsDuplicateFieldsDifferingOnlyByCase(string prefix)
    {
        var probe = prefix.EndsWith("vciMutationProbe\":", StringComparison.Ordinal)
            && prefix.Contains("\"Method\"", StringComparison.Ordinal)
                ? ValidProbe
                : ValidProbe.Insert(1, "\"RunId\":\"duplicate\",");
        var error = VciMutationProbeJsonBoundary.Validate(prefix + probe + "}");

        Assert.NotNull(error);
        Assert.Contains("duplicate", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\"mode\":1", "mode")]
    [InlineData("\"workspaceRoot\":false", "workspaceRoot")]
    [InlineData("\"rollbackTransaction\":\"false\"", "rollbackTransaction")]
    [InlineData("\"maxGroups\":1.5", "maxGroups")]
    [InlineData("\"maxMappings\":2147483648", "maxMappings")]
    public void Validate_RejectsWrongScalarTypes(string replacement, string field)
    {
        var pattern = field switch
        {
            "mode" => "\"mode\":\"Inventory\"",
            "workspaceRoot" => "\"workspaceRoot\":\"C:\\\\vci-root\"",
            _ => string.Empty,
        };
        var probe = string.IsNullOrEmpty(pattern)
            ? ValidProbe.Insert(ValidProbe.Length - 1, "," + replacement)
            : ValidProbe.Replace(pattern, replacement, StringComparison.Ordinal);

        var error = VciMutationProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains(field, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("workspace", "[]")]
    [InlineData("engineeringObject", "[]")]
    [InlineData("mapping", "[]")]
    public void Validate_RejectsNonObjectSelectors(string field, string value)
    {
        var probe = ValidProbe.Insert(ValidProbe.Length - 1, $",\"{field}\":{value}");

        var error = VciMutationProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains(field, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"workspace\":{\"workspaceName\":\"ws\",\"methodName\":\"Delete\"}", "methodName")]
    [InlineData("\"engineeringObject\":{\"structuralPath\":[],\"propertyName\":\"RootPath\"}", "propertyName")]
    [InlineData("\"mapping\":{\"workspace\":{},\"engineeringObject\":{},\"callSequence\":[]}", "callSequence")]
    public void Validate_RejectsUnknownNestedSelectorFields(string selector, string unknownField)
    {
        var probe = unknownField == "propertyName"
            ? ValidProbe.Replace(
                "\"engineeringObject\":{\"structuralPath\":[]}",
                selector,
                StringComparison.Ordinal)
            : ValidProbe.Insert(ValidProbe.Length - 1, "," + selector);

        var error = VciMutationProbeJsonBoundary.Validate(Wrap(probe));

        Assert.NotNull(error);
        Assert.Contains(unknownField, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsWrongSchemaAndUnknownCaseSemantically()
    {
        Assert.Contains(
            "schemaVersion",
            VciMutationProbeJsonBoundary.Validate(Wrap(
                ValidProbe.Replace("vci-mutation-probe/v1", "v2", StringComparison.Ordinal)))!,
            StringComparison.Ordinal);
        Assert.Contains(
            "caseId",
            VciMutationProbeJsonBoundary.Validate(Wrap(
                ValidProbe.Replace("P-INVENTORY", "M-ARBITRARY", StringComparison.Ordinal)))!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AllowsExplicitNullCaseSelectors()
    {
        var probe = ValidProbe
            .Replace("P-INVENTORY", "N-OBJECT-NULL", StringComparison.Ordinal)
            .Replace("Inventory", "Apply", StringComparison.Ordinal)
            .Replace("{\"structuralPath\":[]}", "null", StringComparison.Ordinal)
            .Replace(",\"fileFormat\":\"SimaticML\"", string.Empty, StringComparison.Ordinal);

        Assert.Null(VciMutationProbeJsonBoundary.Validate(Wrap(probe)));
    }

    private static string Wrap(string probe)
        => "{\"method\":\"probe_vci_mutation_contract\",\"projectPath\":\"C:\\\\fixture.ap21\"," +
            "\"vciMutationProbe\":" + probe + "}";
}
