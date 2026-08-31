using TiaMcpServer.Contracts;

namespace TiaMcpServer.Worker;

/// <summary>
/// Result of one internal hardware-page worker call together with the host binding snapshot
/// captured inside the serialized binding operation that issued it.
/// </summary>
public sealed record HardwarePageWorkerCallResult(
    WorkerCallResult WorkerResult,
    ProjectBindingSnapshot HostBinding);
