using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;

namespace TiaMcpServer.Network;

internal sealed record HardwarePageCursorState(
    int Version,
    string ResolvedProjectPath,
    WorkerSessionIdentity SessionIdentity,
    ProjectBindingCursorState HostBinding,
    string QueryHash,
    int OrderingVersion,
    string SnapshotHash,
    int Offset);
