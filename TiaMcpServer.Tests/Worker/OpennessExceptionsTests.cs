using Siemens.Engineering;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Network;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class OpennessExceptionsTests
{
    [Fact]
    public void BroadRecovery_ExcludesPortalLossWithoutNarrowingOrdinaryFailures()
    {
        Assert.False(typeof(EngineeringException).IsAssignableFrom(typeof(NonRecoverableException)));
        Assert.False(OpennessExceptions.IsRecoverable(new NonRecoverableException("closed")));
        Assert.True(OpennessExceptions.IsRecoverable(new EngineeringException("ordinary")));
        Assert.True(OpennessExceptions.IsRecoverable(new InvalidOperationException("metadata failure")));
    }

    [Fact]
    public void PropertyRecovery_RemainsLimitedToEngineeringFailures()
    {
        Assert.False(NetworkOpennessRead.IsRecoverable(new NonRecoverableException("closed")));
        Assert.False(NetworkOpennessRead.IsRecoverable(new InvalidOperationException("programming failure")));
        Assert.True(NetworkOpennessRead.IsRecoverable(new EngineeringNotSupportedException("unavailable")));
    }
}
