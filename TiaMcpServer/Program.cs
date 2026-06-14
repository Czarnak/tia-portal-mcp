using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer
{
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.AddSingleton(new ProjectSessionBinding(ResolveStartupProjectPath(args)));
            builder.Services.AddSingleton<OpennessWorkerClient>();
            builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
            await builder.Build().RunAsync();
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
