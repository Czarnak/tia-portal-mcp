using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using TiaMcpServer.Cli;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
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

            var builder = Host.CreateApplicationBuilder(args);
            builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.AddSingleton(new ProjectSessionBinding(ResolveStartupProjectPath(args)));
            builder.Services.AddSingleton(new WriteSafetyService());
            builder.Services.AddSingleton(sp => new OpennessWorkerClient(
                sp.GetRequiredService<ProjectSessionBinding>(),
                sp.GetRequiredService<ILogger<OpennessWorkerClient>>()));
            builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
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
