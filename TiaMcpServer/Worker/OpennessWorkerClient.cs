using Microsoft.Extensions.Logging;
using TiaMcpServer.Cli;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Worker;

public partial class OpennessWorkerClient : IDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(WorkerTimeoutParser.DefaultMinutes);

    private readonly ProjectSessionBinding _projectSessionBinding;
    private readonly ILogger<OpennessWorkerClient>? _logger;
    private readonly string? _workerExecutablePathOverride;
    private readonly TimeSpan _requestTimeout;
    private readonly Safety.OperationAccessPolicy _accessPolicy;
    private readonly object _transportLock = new();
    // Never disposed: an operation still running at shutdown calls Release() in its finally.
    private readonly SemaphoreSlim _bindingOperationGate = new(1, 1);
    private readonly AsyncLocal<BindingOperationContext?> _bindingOperationContext = new();
    private PersistentWorkerTransport? _transport;
    private bool _disposed;
    private (ProjectBindingSnapshot Binding, WorkerSessionIdentity Identity)? _emptyPortalRecovery;

    public OpennessWorkerClient(
        ProjectSessionBinding projectSessionBinding,
        ILogger<OpennessWorkerClient>? logger = null,
        string? workerExecutablePath = null,
        TimeSpan? requestTimeout = null,
        Safety.OperationAccessPolicy? accessPolicy = null)
    {
        _projectSessionBinding = projectSessionBinding;
        _logger = logger;
        _workerExecutablePathOverride = workerExecutablePath;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
        _accessPolicy = accessPolicy ?? new Safety.OperationAccessPolicy(McpAccessMode.ReadWrite);
    }

    /// <summary>The current access mode policy; every worker request is authorized against it.</summary>
    public Safety.OperationAccessPolicy AccessPolicy => _accessPolicy;
}
