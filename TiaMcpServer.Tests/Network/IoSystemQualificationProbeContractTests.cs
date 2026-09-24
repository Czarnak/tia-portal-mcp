using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class IoSystemQualificationProbeContractTests
{
    private const string Method = "probe_io_system_qualification";

    private static WorkerRequest Baseline() => new()
    {
        Method = Method,
        Confirm = true,
        ExpectedSessionIdentity = new WorkerSessionIdentity { WorkerSessionId = "session" },
        IoSystemQualification = new IoSystemQualificationProbeInfo
        {
            Mode = "compileBaseline",
            Target = new NetworkObjectSelectorInfo
            {
                Kind = NetworkObjectKinds.IoSystem,
                SubnetId = "subnet-a",
                Number = 1
            }
        }
    };

    private static WorkerRequest SetAndCompile(string name, IoSystemQualificationScalarInfo expected,
        IoSystemQualificationScalarInfo desired)
    {
        var request = Baseline();
        request.IoSystemQualification!.Mode = "setAndCompile";
        request.IoSystemQualification.AttributeName = name;
        request.IoSystemQualification.ExpectedValue = expected;
        request.IoSystemQualification.DesiredValue = desired;
        return request;
    }

    [Fact]
    public void WorkerOnlyMethodHasMutationPolicyAndIsAbsentFromPublicNetworkCatalog()
    {
        Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(Method));
        Assert.DoesNotContain(NetworkOperationCatalog.All, operation => operation.Name == Method);
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, Method));
    }

    [Fact]
    public void ValidBaselineIsAccepted()
        => Assert.Null(IoSystemQualificationProbeValidator.Validate(Baseline()));

    [Theory]
    [InlineData("Name")]
    [InlineData("Number")]
    [InlineData("MultipleUseIoSystem")]
    [InlineData("UseIoSystemNameAsDeviceNameExtension")]
    [InlineData("MaxNumberIWlanLinksPerSegment")]
    public void ClosedAttributesAcceptMatchingDifferentScalars(string name)
    {
        var request = name switch
        {
            "Name" => SetAndCompile(name, new() { Kind = "string", StringValue = "before" }, new() { Kind = "string", StringValue = "after" }),
            "Number" or "MaxNumberIWlanLinksPerSegment" => SetAndCompile(name, new() { Kind = "integer", IntegerValue = 1 }, new() { Kind = "integer", IntegerValue = 2 }),
            _ => SetAndCompile(name, new() { Kind = "boolean", BooleanValue = false }, new() { Kind = "boolean", BooleanValue = true })
        };
        Assert.Null(IoSystemQualificationProbeValidator.Validate(request));
    }

    [Theory]
    [InlineData("compilebaseline")]
    [InlineData("setandcompile")]
    [InlineData("")]
    public void UnsupportedModesAreRejected(string mode)
    {
        var request = Baseline();
        request.IoSystemQualification!.Mode = mode;
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));
    }

    [Fact]
    public void ConfirmationAndExpectedIdentityAreRequired()
    {
        var request = Baseline();
        request.Confirm = false;
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));
        request.Confirm = true;
        request.ExpectedSessionIdentity = null;
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));
    }

    [Theory]
    [InlineData("node", "subnet-a", 1)]
    [InlineData("ioSystem", "", 1)]
    [InlineData("ioSystem", "subnet-a", null)]
    [InlineData("ioSystem", "subnet-a", -1)]
    public void InvalidTargetIdentityIsRejected(string kind, string subnetId, int? number)
    {
        var request = Baseline();
        request.IoSystemQualification!.Target!.Kind = kind;
        request.IoSystemQualification.Target.SubnetId = subnetId;
        request.IoSystemQualification.Target.Number = number;
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));
    }

    [Fact]
    public void ExtraSelectorEvidenceIsRejected()
    {
        var request = Baseline();
        request.IoSystemQualification!.Target!.IoSystemName = "extra";
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("name")]
    [InlineData("")]
    public void UnsupportedAttributeIsRejected(string attributeName)
    {
        var request = SetAndCompile(attributeName,
            new() { Kind = "string", StringValue = "before" },
            new() { Kind = "string", StringValue = "after" });
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));
    }

    [Fact]
    public void MissingMismatchedOrExtraScalarMembersAreRejected()
    {
        var request = SetAndCompile("Name", new() { Kind = "integer", IntegerValue = 1 },
            new() { Kind = "integer", IntegerValue = 2 });
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));

        request = SetAndCompile("Name", new() { Kind = "string" }, new() { Kind = "string", StringValue = "after" });
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));

        request = SetAndCompile("Name", new() { Kind = "string", StringValue = "before", IntegerValue = 1 },
            new() { Kind = "string", StringValue = "after" });
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));

        request = SetAndCompile("Name", new() { Kind = "string", StringValue = "before" },
            new() { Kind = "integer", IntegerValue = 2 });
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));
    }

    [Fact]
    public void EqualExpectedAndDesiredValuesAreRejected()
    {
        var request = SetAndCompile("Number", new() { Kind = "integer", IntegerValue = 2 },
            new() { Kind = "integer", IntegerValue = 2 });
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));
    }

    [Fact]
    public void BaselineRejectsValueFields()
    {
        var request = Baseline();
        request.IoSystemQualification!.AttributeName = "Name";
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));
        request.IoSystemQualification.AttributeName = null;
        request.IoSystemQualification.ExpectedValue = new() { Kind = "string", StringValue = "before" };
        Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));
    }
}
