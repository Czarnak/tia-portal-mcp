using TiaMcpServer.Cursors;
using TiaMcpServer.Network;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.TestSupport;

internal static class NetworkReadTestFactory
{
    internal static NetworkReadOperationExecutor Create(
        OpennessWorkerClient client,
        AuthenticatedCursorProtector protector)
    {
        var hardwareCodec = new HardwarePageCursorCodec(protector);
        var objectCodec = new NetworkObjectPageCursorCodec(protector);
        return new NetworkReadOperationExecutor(client,
            new HardwarePaginationCoordinator(client, hardwareCodec, new HardwarePageProjector(hardwareCodec)),
            new NetworkObjectPaginationCoordinator(client, objectCodec));
    }
}
