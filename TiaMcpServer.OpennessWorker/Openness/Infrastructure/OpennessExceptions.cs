using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness.Infrastructure;

internal static class OpennessExceptions
{
    internal static bool IsRecoverable(Exception exception)
        => exception is not NonRecoverableException;
}
