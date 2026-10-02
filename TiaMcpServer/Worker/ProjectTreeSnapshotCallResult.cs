using TiaMcpServer.Contracts;

namespace TiaMcpServer.Worker;

/// <summary>
/// Internal snapshot result and host binding captured after the browse inside its serialized operation.
/// </summary>
public sealed record ProjectTreeSnapshotCallResult(
    WorkerCallResult WorkerResult,
    ProjectBindingSnapshot HostBinding);
