using System.IO.Pipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests;

/// <summary>
/// Runs a real MCP server and a real MCP client in-process over a pair of anonymous pipes, with
/// the tool type registered exactly as <c>Program.cs</c> registers it and an
/// <see cref="OpennessWorkerClient"/> pointed at the scripted FakeWorker.
///
/// <para>
/// The point is that tests must observe what an MCP client actually receives — the advertised
/// <c>outputSchema</c> from <c>tools/list</c> and the <c>content</c>/<c>structuredContent</c> pair
/// from <c>tools/call</c>. Invoking the attributed tool method directly cannot see any of that:
/// schema advertisement and result marshalling are done by the SDK, not by the method body.
/// </para>
/// </summary>
internal sealed class McpProtocolTestHarness : IAsyncDisposable
{
    private readonly AnonymousPipeServerStream _clientWrites;
    private readonly AnonymousPipeClientStream _serverReads;
    private readonly AnonymousPipeServerStream _serverWrites;
    private readonly AnonymousPipeClientStream _clientReads;
    private readonly IServiceProvider _services;
    private readonly McpServer _server;
    private readonly Task _serverLoop;
    private readonly CancellationTokenSource _cancellation;

    private McpProtocolTestHarness(
        AnonymousPipeServerStream clientWrites,
        AnonymousPipeClientStream serverReads,
        AnonymousPipeServerStream serverWrites,
        AnonymousPipeClientStream clientReads,
        IServiceProvider services,
        McpServer server,
        Task serverLoop,
        CancellationTokenSource cancellation,
        McpClient client,
        OpennessWorkerClient workerClient)
    {
        _clientWrites = clientWrites;
        _serverReads = serverReads;
        _serverWrites = serverWrites;
        _clientReads = clientReads;
        _services = services;
        _server = server;
        _serverLoop = serverLoop;
        _cancellation = cancellation;
        Client = client;
        WorkerClient = workerClient;
    }

    /// <summary>The connected MCP client. Tests must go through it, never through the tool method.</summary>
    public McpClient Client { get; }

    public OpennessWorkerClient WorkerClient { get; }

    /// <summary>
    /// Starts a server exposing <typeparamref name="TTools"/> and returns a connected client.
    /// <paramref name="auditDirectory"/> redirects write-safety audit records away from the real
    /// per-user audit location, so a protocol test that applies a write cannot pollute it.
    /// </summary>
    public static Task<McpProtocolTestHarness> StartAsync<TTools>(
        string? auditDirectory = null,
        string? startupProjectPath = null)
        where TTools : class
        => StartAsync(
            McpAccessMode.ReadWrite,
            builder => builder.WithTools<TTools>(),
            auditDirectory,
            startupProjectPath);

    /// <summary>
    /// Starts a server exposing BOTH <typeparamref name="TTools1"/> and <typeparamref name="TTools2"/>
    /// on one session - and therefore one FakeWorker process - so a test can drive a read, a write,
    /// and a follow-up read against the same process-local scripted worker state (e.g. proving a
    /// multi-homed configure through network_write and observing it through network_read).
    /// </summary>
    public static Task<McpProtocolTestHarness> StartAsync<TTools1, TTools2>(
        string? auditDirectory = null,
        string? startupProjectPath = null)
        where TTools1 : class
        where TTools2 : class
        => StartAsync(
            McpAccessMode.ReadWrite,
            builder => builder.WithTools<TTools1>().WithTools<TTools2>(),
            auditDirectory,
            startupProjectPath);

    public static Task<McpProtocolTestHarness> StartAsync(
        McpAccessMode accessMode,
        Action<IMcpServerBuilder> registerTools,
        string? auditDirectory = null,
        string? startupProjectPath = null)
        => StartCoreAsync(accessMode, registerTools, auditDirectory, startupProjectPath);

    public static Task<McpProtocolTestHarness> StartProductionSurfaceAsync(
        McpAccessMode accessMode,
        string? auditDirectory = null,
        string? startupProjectPath = null)
        => StartAsync(
            accessMode,
            builder =>
            {
                builder.WithTools<ProjectReadTools>()
                       .WithTools<ReadBatchTools>()
                       .WithTools<NetworkReadTools>();

                if (accessMode == McpAccessMode.ReadWrite)
                {
                    builder.WithTools<ProjectEngineeringTools>()
                           .WithTools<ProjectWriteTools>()
                           .WithTools<WriteBatchTools>()
                           .WithTools<NetworkWriteTools>();
                }
            },
            auditDirectory,
            startupProjectPath);

    private static async Task<McpProtocolTestHarness> StartCoreAsync(
        McpAccessMode accessMode,
        Action<IMcpServerBuilder> registerTools,
        string? auditDirectory,
        string? startupProjectPath)
    {
        var clientWrites = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var serverReads = new AnonymousPipeClientStream(
            PipeDirection.In, clientWrites.ClientSafePipeHandle);
        var serverWrites = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var clientReads = new AnonymousPipeClientStream(
            PipeDirection.In, serverWrites.ClientSafePipeHandle);

        var binding = new ProjectSessionBinding(null);
        var accessPolicy = new OperationAccessPolicy(accessMode);
        var workerClient = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: accessPolicy);

        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton(binding);
        collection.AddSingleton(accessPolicy);
        collection.AddSingleton(workerClient);
        collection.AddSingleton(new WriteSafetyService(
            binding,
            () => DateTimeOffset.UtcNow,
            WriteSafetyService.DefaultTokenLifetime,
            auditDirectory));
        registerTools(collection.AddMcpServer());
        var services = collection.BuildServiceProvider();

        var server = McpServer.Create(
            new StreamServerTransport(serverReads, serverWrites),
            services.GetRequiredService<IOptions<McpServerOptions>>().Value,
            loggerFactory: null,
            serviceProvider: services);

        var cancellation = new CancellationTokenSource();
        var serverLoop = server.RunAsync(cancellation.Token);

        var client = await McpClient.CreateAsync(
            new StreamClientTransport(serverInput: clientWrites, serverOutput: clientReads));

        if (!string.IsNullOrWhiteSpace(startupProjectPath))
        {
            await NetworkVerifiedWriteFixture.VerifyAsync(workerClient, binding, startupProjectPath)
                .ConfigureAwait(false);
        }

        return new McpProtocolTestHarness(
            clientWrites,
            serverReads,
            serverWrites,
            clientReads,
            services,
            server,
            serverLoop,
            cancellation,
            client,
            workerClient);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        await Client.DisposeAsync();
        await _server.DisposeAsync();
        try
        {
            await _serverLoop;
        }
        catch (OperationCanceledException)
        {
        }

        WorkerClient.Dispose();
        _clientWrites.Dispose();
        _serverReads.Dispose();
        _serverWrites.Dispose();
        _clientReads.Dispose();
        _cancellation.Dispose();
        (_services as IDisposable)?.Dispose();
    }
}
