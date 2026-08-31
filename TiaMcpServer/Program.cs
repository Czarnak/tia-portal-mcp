using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using TiaMcpServer.Batch;
using TiaMcpServer.Cli;
using TiaMcpServer.Cli.Install;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "install", StringComparison.OrdinalIgnoreCase))
            {
                return await InstallCommand.RunAsync(args[1..]);
            }

            if (args.Length > 0 && string.Equals(args[0], "doctor", StringComparison.OrdinalIgnoreCase))
            {
                return await DoctorCommand.RunAsync(args[1..]);
            }

            if (args.Length > 0 && (string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(args[0], "-v", StringComparison.OrdinalIgnoreCase)))
            {
                return VersionCommand.Run(Console.Out);
            }

            var accessModeResult = AccessModeParser.Parse(args);
            if (!accessModeResult.IsValid)
            {
                Console.Error.WriteLine($"Error: {accessModeResult.Error}");
                return 1;
            }

            var accessMode = accessModeResult.Mode;
            var accessPolicy = new OperationAccessPolicy(accessMode);
            if (!TryResolveStartupProjectPath(args, out var startupProjectPath, out var projectPathError))
            {
                Console.Error.WriteLine($"Error: {projectPathError}");
                return 1;
            }

            Console.Error.WriteLine($"TIA MCP access mode: {accessMode.ToString().ToUpperInvariant()}");
            if (accessMode == McpAccessMode.ReadOnly)
            {
                Console.Error.WriteLine("Project opening, compilation, writes, lifecycle operations, and PLC control are disabled.");
            }

            // Application-specific access-mode switches have already been parsed. Remove them
            // before generic-host configuration processes the remaining command line because
            // value-less switches such as --read-only are not valid configuration key/value pairs.
            var builder = Host.CreateApplicationBuilder(
                HostArgumentFilter.RemoveAccessModeArguments(args));
            builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.AddSingleton(new ProjectSessionBinding(startupProjectPath));
            builder.Services.AddSingleton(sp => new WriteSafetyService(
                sp.GetRequiredService<ProjectSessionBinding>()));
            builder.Services.AddSingleton(accessPolicy);
            builder.Services.AddSingleton(sp => new OpennessWorkerClient(
                sp.GetRequiredService<ProjectSessionBinding>(),
                sp.GetRequiredService<ILogger<OpennessWorkerClient>>(),
                accessPolicy: sp.GetRequiredService<OperationAccessPolicy>()));
            builder.Services.AddSingleton(NetworkReadTools.ProcessCursorCodec);
            builder.Services.AddSingleton(sp => new HardwarePageProjector(
                sp.GetRequiredService<HardwarePageCursorCodec>()));
            builder.Services.AddSingleton(sp => new HardwarePaginationCoordinator(
                sp.GetRequiredService<OpennessWorkerClient>(),
                sp.GetRequiredService<HardwarePageCursorCodec>(),
                sp.GetRequiredService<HardwarePageProjector>()));
            builder.Services.AddSingleton(sp => new NetworkReadOperationExecutor(
                sp.GetRequiredService<OpennessWorkerClient>(),
                sp.GetRequiredService<HardwarePaginationCoordinator>()));

            var mcp = builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithTools<ProjectReadTools>()
                .WithTools<ReadBatchTools>()
                .WithTools<NetworkReadTools>();

            if (accessMode == McpAccessMode.ReadWrite)
            {
                mcp.WithTools<ProjectEngineeringTools>()
                   .WithTools<ProjectWriteTools>()
                   .WithTools<WriteBatchTools>()
                   .WithTools<NetworkWriteTools>();
            }

            using var host = builder.Build();
            NetworkReadTools.RegisterExecutor(
                host.Services.GetRequiredService<OpennessWorkerClient>(),
                host.Services.GetRequiredService<NetworkReadOperationExecutor>());
            await host.RunAsync();
            return 0;
        }

        private static bool TryResolveStartupProjectPath(
            string[] args,
            out string? projectPath,
            out string? error)
        {
            projectPath = null;
            error = null;
            for (int i = 0; i < args.Length; i++)
            {
                string? candidate = null;
                if (string.Equals(args[i], "--project", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length ||
                        string.IsNullOrWhiteSpace(args[i + 1]) ||
                        args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        error = "--project requires a value.";
                        return false;
                    }

                    candidate = args[++i];
                }
                else
                {
                    const string projectPrefix = "--project=";
                    if (args[i].StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        candidate = args[i].Substring(projectPrefix.Length);
                        if (string.IsNullOrWhiteSpace(candidate))
                        {
                            error = "--project requires a value.";
                            return false;
                        }
                    }
                }

                if (candidate is null)
                {
                    continue;
                }

                if (projectPath is not null)
                {
                    error = "--project may be specified only once.";
                    return false;
                }

                projectPath = candidate;
            }

            projectPath ??= Environment.GetEnvironmentVariable("TIA_MCP_PROJECT_PATH");
            return true;
        }
    }
}
