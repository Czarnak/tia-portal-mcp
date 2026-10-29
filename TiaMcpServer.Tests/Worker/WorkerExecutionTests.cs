using Siemens.Engineering;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class WorkerExecutionTests
{
    [Theory]
    [InlineData(true, true, WorkerFailureCategories.EngineeringRejected)]
    [InlineData(true, false, WorkerFailureCategories.WorkerOperationFailed)]
    [InlineData(false, true, WorkerFailureCategories.WorkerOperationFailed)]
    public void Classify_UsesExceptionTypeAndMutationFlag(bool targetInvocation, bool mutates, string expected)
    {
        EngineeringException exception = targetInvocation
            ? new EngineeringTargetInvocationException("m") : new EngineeringException("m");
        Assert.Equal(expected, WorkerExecution.Classify(exception, mutates));
    }

    [Fact]
    public void WrappedEngineeringFailure_AfterPortalDisposal_MapsToPortalLostAndKeepsOutcome()
    {
        var revision = 0L;
        var outcome = new ConfigureNetworkDeviceResultInfo
        {
            DeviceName = "PLC_1", AppliedSettings = { ["Address"] = "10.0.0.1" }
        };
        var exception = new WorkerOperationException(WorkerFailureCategories.EngineeringRejected, "x",
            engineeringError: new EngineeringErrorInfo { Type = "EngineeringTargetInvocationException", Message = "gone" },
            configureNetworkDeviceOutcome: outcome);
        var result = WorkerExecution.Execute(() => { revision++; throw exception; }, () => revision, mutates: true);

        Assert.Equal(WorkerFailureCategories.PortalLost, result.FailureCategory);
        Assert.Null(result.EngineeringError);
        Assert.Same(outcome, result.ConfigureNetworkDeviceOutcome);
        Assert.Equal("10.0.0.1", result.ConfigureNetworkDeviceOutcome!.AppliedSettings["Address"]);
    }

    [Fact]
    public void WrappedEngineeringFailure_WithoutDisposal_IsRethrown()
    {
        var exception = new WorkerOperationException(WorkerFailureCategories.EngineeringRejected, "x",
            engineeringError: new EngineeringErrorInfo { Type = "EngineeringTargetInvocationException", Message = "gone" });
        var thrown = Assert.Throws<WorkerOperationException>(() => WorkerExecution.Execute(() => throw exception, () => 0, mutates: true));
        Assert.Same(exception, thrown);
    }

    [Fact]
    public void WrappedHmiFailure_AfterPortalDisposal_MapsToPortalLostAndKeepsOutcome()
    {
        var revision = 0L;
        var outcome = new TiaMcpServer.Contracts.Hmi.HmiWriteOutcomeInfo { ObjectName = "T" };
        outcome.AppliedProperties.Add("Create");
        var exception = new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "x", hmiWriteOutcome: outcome);
        var result = WorkerExecution.Execute(() => { revision++; throw exception; }, () => revision, mutates: true);

        Assert.Equal(WorkerFailureCategories.PortalLost, result.FailureCategory);
        Assert.Null(result.EngineeringError);
        Assert.Same(outcome, result.HmiWriteOutcome);
    }

    [Fact]
    public void WrappedHmiFailure_WithoutDisposal_IsRethrown()
    {
        var exception = new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "x",
            hmiWriteOutcome: new TiaMcpServer.Contracts.Hmi.HmiWriteOutcomeInfo { ObjectName = "T" });
        var thrown = Assert.Throws<WorkerOperationException>(() => WorkerExecution.Execute(() => throw exception, () => 0, mutates: true));
        Assert.Same(exception, thrown);
    }

    [Fact]
    public void WriteLookupFailure_AfterPortalDisposal_MapsToPortalLost()
    {
        var revision = 0L;
        var software = new Siemens.Engineering.HmiUnified.HmiSoftware();
        software.Tags.FindOverride = _ => { revision++; throw new EngineeringException("Access to a disposed object is not possible."); };
        var result = WorkerExecution.Execute(() =>
        {
            TiaMcpServer.OpennessWorker.Openness.Hmi.HmiTagWriter.Delete(software, new() { TagName = "T" });
            return new WorkerResponse { Success = true };
        }, () => revision, mutates: true);

        Assert.Equal(WorkerFailureCategories.PortalLost, result.FailureCategory);
        Assert.Null(result.EngineeringError);
    }

    [Fact]
    public void WrappedFailureWithoutEngineeringError_IsNeverIntercepted()
    {
        var revision = 0L;
        var exception = new WorkerOperationException(WorkerFailureCategories.ValidationError, "v");
        var thrown = Assert.Throws<WorkerOperationException>(() => WorkerExecution.Execute(
            () => { revision++; throw exception; }, () => revision, mutates: true));
        Assert.Same(exception, thrown);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void EngineeringFailure_WithoutNewDisposalRemainsOrdinary(long revision)
    {
        var result = WorkerExecution.Execute(
            () => throw new EngineeringException("Access to a disposed object is not possible."), () => revision);

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.FailureCategory);
        Assert.Equal("TIA Portal operation failed: Access to a disposed object is not possible.", result.Error);
    }

    [Fact]
    public void MutationTargetInvocationFailure_MapsToEngineeringRejectedWithVerbatimChain()
    {
        var root = new InvalidOperationException("root cause");
        var middle = new EngineeringException("middle", root);
        var result = WorkerExecution.Execute(
            () => throw new EngineeringTargetInvocationException("Siemens text", middle), () => 0, mutates: true);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, result.FailureCategory);
        Assert.Equal("TIA Portal rejected the operation: Siemens text", result.Error);
        var info = Assert.IsType<EngineeringErrorInfo>(result.EngineeringError);
        Assert.Equal("EngineeringTargetInvocationException", info.Type);
        Assert.Equal("Siemens text", info.Message);
        Assert.Collection(info.Inner,
            first => { Assert.Equal("EngineeringException", first.Type); Assert.Equal("middle", first.Message); },
            second => { Assert.Equal("InvalidOperationException", second.Type); Assert.Equal("root cause", second.Message); });
    }

    [Fact]
    public void MutationOtherEngineeringFailure_StaysOrdinaryButCarriesTheError()
    {
        var result = WorkerExecution.Execute(
            () => throw new EngineeringNotSupportedException("not supported here"), () => 0, mutates: true);

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.FailureCategory);
        var info = Assert.IsType<EngineeringErrorInfo>(result.EngineeringError);
        Assert.Equal("EngineeringNotSupportedException", info.Type);
        Assert.Equal("not supported here", info.Message);
        Assert.Empty(info.Inner);
    }

    [Fact]
    public void ReadTargetInvocationFailure_NeverMapsToEngineeringRejected()
    {
        var result = WorkerExecution.Execute(
            () => throw new EngineeringTargetInvocationException("read failed"), () => 0);

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.FailureCategory);
        Assert.Equal("TIA Portal operation failed: read failed", result.Error);
        Assert.Equal("read failed", result.EngineeringError!.Message);
    }

    [Fact]
    public void MutationTargetInvocationFailure_AfterPortalDisposal_StillMapsToPortalLost()
    {
        var revision = 0L;
        var result = WorkerExecution.Execute(
            () => { revision++; throw new EngineeringTargetInvocationException("gone"); }, () => revision, mutates: true);

        Assert.Equal(WorkerFailureCategories.PortalLost, result.FailureCategory);
        Assert.Null(result.EngineeringError);
    }

    [Fact]
    public void NonEngineeringFailures_CarryNoEngineeringError()
    {
        var result = WorkerExecution.Execute(() => throw new InvalidOperationException("plain"), () => 0, mutates: true);

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.FailureCategory);
        Assert.Null(result.EngineeringError);
    }

    [Fact]
    public void EngineeringError_BoundsChainDepthAndMessageLength()
    {
        Exception chain = new InvalidOperationException(new string('z', 10_000));
        for (var depth = 0; depth < 20; depth++) chain = new EngineeringException($"level {depth}", chain);

        var result = WorkerExecution.Execute(() => throw new EngineeringTargetInvocationException("top", chain), () => 0, mutates: true);

        var info = result.EngineeringError!;
        Assert.Equal(EngineeringErrorInfo.MaxInnerCount, info.Inner.Count);
        Assert.All(info.Inner, inner => Assert.True(inner.Message.Length <= EngineeringErrorInfo.MaxMessageChars));
    }

    [Fact]
    public void NonRecoverableFailure_AlwaysMapsToPortalLost()
    {
        var result = WorkerExecution.Execute(() => throw new NonRecoverableException("lost"), () => 0);
        Assert.Equal(WorkerFailureCategories.PortalLost, result.FailureCategory);
        Assert.Contains("An attempted mutation may have completed", result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturnedOutcome_IsPreservedAfterDisposal(bool success)
    {
        var revision = 0L;
        var expected = new WorkerResponse { Success = success, Payload = "typed outcome", FailureCategory = "postcondition_failed" };
        var result = WorkerExecution.Execute(() => { revision++; return expected; }, () => revision);
        Assert.Same(expected, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OtherMappedFailures_KeepTheirCategoryAfterDisposal(bool io)
    {
        var revision = 0L;
        Exception error = io ? new IOException("io") : new InvalidOperationException("invalid");
        var result = WorkerExecution.Execute(() => { revision++; throw error; }, () => revision);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.FailureCategory);
        Assert.Equal(error.Message, result.Error);
    }
}
