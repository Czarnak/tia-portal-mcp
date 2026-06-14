using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools
{
    [McpServerToolType]
    public static class AddNetworkDeviceTool
    {
        [McpServerTool(Name = "preview_add_network_device")]
        [Description("Preview adding a device from the hardware catalog and return a short-lived safetyToken. Pass the token to add_network_device after reviewing the preview.")]
        public static async Task<string> PreviewAddNetworkDevice(
            OpennessWorkerClient workerClient,
            [Description("Exact catalog type identifier from search_equipment_catalog.")] string typeIdentifier,
            [Description("Name for the new device.")] string deviceName,
            [Description("Name for the root device item. Defaults to deviceName when omitted.")] string? deviceItemName = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            var effectiveDeviceItemName = string.IsNullOrWhiteSpace(deviceItemName)
                ? deviceName
                : deviceItemName;
            var currentState = await workerClient.ReadHardwareConfigAsync(projectPath).ConfigureAwait(false);
            var target = new { deviceName };
            var requestedInput = new { typeIdentifier, deviceName, deviceItemName = effectiveDeviceItemName };

            return WriteSafetyTooling.CreatePreview(
                "add_network_device",
                projectPath,
                target,
                $"Add network device '{deviceName}' using catalog type '{typeIdentifier}'.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "add_network_device")]
        [Description("Insert a device from the TIA Portal hardware catalog into the project. Requires confirm=true and a safetyToken from preview_add_network_device.")]
        public static async Task<string> AddNetworkDevice(
            OpennessWorkerClient workerClient,
            [Description("Exact catalog type identifier from search_equipment_catalog.")] string typeIdentifier,
            [Description("Name for the new device.")] string deviceName,
            [Description("Name for the root device item. Defaults to deviceName when omitted.")] string? deviceItemName = null,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_add_network_device for this exact write request.")] string? safetyToken = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with adding a network device.";
            }

            var effectiveDeviceItemName = string.IsNullOrWhiteSpace(deviceItemName)
                ? deviceName
                : deviceItemName;

            var target = new { deviceName };
            var requestedInput = new { typeIdentifier, deviceName, deviceItemName = effectiveDeviceItemName };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_add_network_device",
                "add_network_device",
                projectPath,
                target,
                requestedInput,
                () => workerClient.ReadHardwareConfigAsync(projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.AddNetworkDeviceAsync(
                typeIdentifier,
                deviceName,
                effectiveDeviceItemName!,
                projectPath).ConfigureAwait(false);
            var hardwareConfig = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.ReadHardwareConfigAsync(projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit(
                "add_network_device",
                projectPath,
                target,
                requestedInput,
                safety.CurrentState,
                result);

            return WriteSafetyTooling.BuildApplyResult("add_network_device", result, "read_hardware_config", hardwareConfig);
        }
    }
}
