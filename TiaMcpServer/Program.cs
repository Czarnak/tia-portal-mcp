using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using TiaMcpServer.Batch;
using TiaMcpServer.Cli;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
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
            builder.Services.AddSingleton(new ProjectSessionBinding(ResolveStartupProjectPath(args)));
            builder.Services.AddSingleton(new WriteSafetyService());
            builder.Services.AddSingleton(accessPolicy);
            builder.Services.AddSingleton(sp => new OpennessWorkerClient(
                sp.GetRequiredService<ProjectSessionBinding>(),
                sp.GetRequiredService<ILogger<OpennessWorkerClient>>(),
                accessPolicy: sp.GetRequiredService<OperationAccessPolicy>()));

            var mcp = builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithTools<ProjectReadTools>()
                .WithTools<ReadBatchTools>();

            if (accessMode == McpAccessMode.ReadWrite)
            {
                mcp.WithTools<ProjectEngineeringTools>()
                   .WithTools<ProjectWriteTools>()
                   .WithTools<WriteBatchTools>();
            }

            await builder.Build().RunAsync();
            return 0;
        }

        private static string? ResolveStartupProjectPath(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--project", StringComparison.OrdinalIgnoreCase) &&
                    i + 1 < args.Length)
                {
                    return args[i + 1];
                }

                const string projectPrefix = "--project=";
                if (args[i].StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(projectPrefix.Length);
                }
            }

            return Environment.GetEnvironmentVariable("TIA_MCP_PROJECT_PATH");
        }
    }
}
