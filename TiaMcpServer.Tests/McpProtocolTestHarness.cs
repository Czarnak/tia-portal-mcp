using System.IO.Pipes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
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

    /// <summary>Starts a server exposing <typeparamref name="TTools"/> and returns a connected client.</summary>
    public static async Task<McpProtocolTestHarness> StartAsync<TTools>()
        where TTools : class
    {
        var clientWrites = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var serverReads = new AnonymousPipeClientStream(
            PipeDirection.In, clientWrites.ClientSafePipeHandle);
        var serverWrites = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var clientReads = new AnonymousPipeClientStream(
            PipeDirection.In, serverWrites.ClientSafePipeHandle);

        var binding = new ProjectSessionBinding(null);
        var accessPolicy = new OperationAccessPolicy(McpAccessMode.ReadWrite);
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
        collection.AddSingleton(new WriteSafetyService());
        collection.AddMcpServer().WithTools<TTools>();
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
