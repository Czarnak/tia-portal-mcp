using Siemens.Engineering;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class WorkerExecutionTests
{
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
