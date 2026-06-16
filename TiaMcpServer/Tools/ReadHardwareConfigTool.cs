using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools
{
    [McpServerToolType]
    public static class ReadHardwareConfigTool
    {
        [McpServerTool(Name = "read_hardware_config")]
        [Description("DEPRECATED (prefer execute_read_batch): Export the hardware configuration and network topology from the TIA Portal project. Returns a JSON document with all devices, their rack modules, network interfaces, IP addresses, PROFINET device names, subnets, and IO systems.")]
        public static async Task<string> ReadHardwareConfig(
            OpennessWorkerClient workerClient,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            return await workerClient.ReadHardwareConfigAsync(projectPath).ConfigureAwait(false);
        }
    }
}
