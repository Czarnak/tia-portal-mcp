using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness.Infrastructure;

internal static class OpennessExceptions
{
    internal static bool IsRecoverable(Exception exception)
        => exception is not NonRecoverableException;

    /// <summary>
    /// Openness reports a property the current object does not support as <see cref="EngineeringTargetInvocationException"/>
    /// or <see cref="EngineeringNotSupportedException"/>; only those may become a per-property null. Anything else
    /// (a disposed object, a dead Portal, a security failure) must fail the whole read.
    /// </summary>
    internal static bool IsRecoverablePropertyFailure(Exception exception)
        => exception is EngineeringTargetInvocationException or EngineeringNotSupportedException;
}
