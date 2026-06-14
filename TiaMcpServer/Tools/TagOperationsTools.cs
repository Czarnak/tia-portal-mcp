using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools
{
    [McpServerToolType]
    public static class TagTableOperationsTool
    {
        [McpServerTool(Name = "preview_create_tag_table")]
        [Description("Preview creating a PLC tag table and return a short-lived safetyToken. Pass the token to create_tag_table after reviewing the preview.")]
        public static async Task<string> PreviewCreateTagTable(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table to create.")] string tableName,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            var currentState = await workerClient.ListTagTablesAsync(plcName, projectPath).ConfigureAwait(false);
            var target = new { plcName, tableName, folderPath };
            var requestedInput = new { plcName, tableName, folderPath };
            return WriteSafetyTooling.CreatePreview(
                "create_tag_table",
                projectPath,
                target,
                $"Create PLC tag table '{tableName}'.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "create_tag_table")]
        [Description("Create a PLC tag table. Requires confirm=true and a safetyToken from preview_create_tag_table.")]
        public static async Task<string> CreateTagTable(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table to create.")] string tableName,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_create_tag_table for this exact write request.")] string? safetyToken = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with creating a tag table.";
            }

            var target = new { plcName, tableName, folderPath };
            var requestedInput = new { plcName, tableName, folderPath };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_create_tag_table",
                "create_tag_table",
                projectPath,
                target,
                requestedInput,
                () => workerClient.ListTagTablesAsync(plcName, projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.CreateTagTableAsync(
                plcName,
                tableName,
                folderPath,
                projectPath).ConfigureAwait(false);
            var compileResult = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.CompileCheckAsync(null, plcName, projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("create_tag_table", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("create_tag_table", result, "compile_check", compileResult);
        }

        [McpServerTool(Name = "preview_delete_tag_table")]
        [Description("Preview deleting a PLC tag table and return a short-lived safetyToken. Pass the token to delete_tag_table after reviewing the preview.")]
        public static async Task<string> PreviewDeleteTagTable(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table to delete.")] string tableName,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            var currentState = await workerClient.ListTagTablesAsync(plcName, projectPath).ConfigureAwait(false);
            var target = new { plcName, tableName, folderPath };
            var requestedInput = new { plcName, tableName, folderPath };
            return WriteSafetyTooling.CreatePreview(
                "delete_tag_table",
                projectPath,
                target,
                $"Delete PLC tag table '{tableName}'.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "delete_tag_table")]
        [Description("Delete a PLC tag table. The default tag table cannot be deleted. Requires confirm=true and a safetyToken from preview_delete_tag_table.")]
        public static async Task<string> DeleteTagTable(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table to delete.")] string tableName,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_delete_tag_table for this exact write request.")] string? safetyToken = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with deleting a tag table.";
            }

            var target = new { plcName, tableName, folderPath };
            var requestedInput = new { plcName, tableName, folderPath };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_delete_tag_table",
                "delete_tag_table",
                projectPath,
                target,
                requestedInput,
                () => workerClient.ListTagTablesAsync(plcName, projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.DeleteTagTableAsync(
                plcName,
                tableName,
                folderPath,
                projectPath).ConfigureAwait(false);
            var compileResult = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.CompileCheckAsync(null, plcName, projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("delete_tag_table", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("delete_tag_table", result, "compile_check", compileResult);
        }
    }

    [McpServerToolType]
    public static class TagOperationsTool
    {
        [McpServerTool(Name = "preview_create_tag")]
        [Description("Preview creating a PLC tag and return a short-lived safetyToken. Pass the token to create_tag after reviewing the preview.")]
        public static async Task<string> PreviewCreateTag(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table containing the tag.")] string tableName,
            [Description("Name of the tag to create.")] string name,
            [Description("TIA Portal data type, such as Bool, Int, Real, or a PLC type name.")] string dataType,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Optional logical address, such as %I0.0. Use empty to leave unassigned.")] string? logicalAddress = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            var currentState = await workerClient.ListTagTablesAsync(plcName, projectPath).ConfigureAwait(false);
            var target = new { plcName, tableName, folderPath, name };
            var requestedInput = new { plcName, tableName, folderPath, name, dataType, logicalAddress };
            return WriteSafetyTooling.CreatePreview(
                "create_tag",
                projectPath,
                target,
                $"Create PLC tag '{name}' in table '{tableName}'.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "create_tag")]
        [Description("Create a PLC tag in an existing tag table. Requires confirm=true and a safetyToken from preview_create_tag.")]
        public static async Task<string> CreateTag(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table containing the tag.")] string tableName,
            [Description("Name of the tag to create.")] string name,
            [Description("TIA Portal data type, such as Bool, Int, Real, or a PLC type name.")] string dataType,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Optional logical address, such as %I0.0. Use empty to leave unassigned.")] string? logicalAddress = null,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_create_tag for this exact write request.")] string? safetyToken = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with creating a tag.";
            }

            var target = new { plcName, tableName, folderPath, name };
            var requestedInput = new { plcName, tableName, folderPath, name, dataType, logicalAddress };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_create_tag",
                "create_tag",
                projectPath,
                target,
                requestedInput,
                () => workerClient.ListTagTablesAsync(plcName, projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.CreateTagAsync(
                plcName,
                tableName,
                folderPath,
                name,
                dataType,
                logicalAddress,
                projectPath).ConfigureAwait(false);
            var compileResult = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.CompileCheckAsync(null, plcName, projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("create_tag", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("create_tag", result, "compile_check", compileResult);
        }

        [McpServerTool(Name = "preview_update_tag")]
        [Description("Preview updating a PLC tag and return a short-lived safetyToken. Pass the token to update_tag after reviewing the preview.")]
        public static async Task<string> PreviewUpdateTag(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table containing the tag.")] string tableName,
            [Description("Current name of the tag to update.")] string name,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Optional new tag name.")] string? newName = null,
            [Description("Optional new TIA Portal data type.")] string? dataType = null,
            [Description("Optional new logical address.")] string? logicalAddress = null,
            [Description("Optional ExternalAccessible flag.")] bool? externalAccessible = null,
            [Description("Optional ExternalVisible flag.")] bool? externalVisible = null,
            [Description("Optional ExternalWritable flag.")] bool? externalWritable = null,
            [Description("Optional IsSafety flag.")] bool? isSafety = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            var currentState = await workerClient.ListTagTablesAsync(plcName, projectPath).ConfigureAwait(false);
            var target = new { plcName, tableName, folderPath, name };
            var requestedInput = new { plcName, tableName, folderPath, name, newName, dataType, logicalAddress, externalAccessible, externalVisible, externalWritable, isSafety };
            return WriteSafetyTooling.CreatePreview(
                "update_tag",
                projectPath,
                target,
                $"Update PLC tag '{name}' in table '{tableName}'.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "update_tag")]
        [Description("Update a PLC tag name, data type, logical address, external access flags, or safety flag. Requires confirm=true and a safetyToken from preview_update_tag.")]
        public static async Task<string> UpdateTag(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table containing the tag.")] string tableName,
            [Description("Current name of the tag to update.")] string name,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Optional new tag name.")] string? newName = null,
            [Description("Optional new TIA Portal data type.")] string? dataType = null,
            [Description("Optional new logical address.")] string? logicalAddress = null,
            [Description("Optional ExternalAccessible flag.")] bool? externalAccessible = null,
            [Description("Optional ExternalVisible flag.")] bool? externalVisible = null,
            [Description("Optional ExternalWritable flag.")] bool? externalWritable = null,
            [Description("Optional IsSafety flag.")] bool? isSafety = null,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_update_tag for this exact write request.")] string? safetyToken = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with updating a tag.";
            }

            var target = new { plcName, tableName, folderPath, name };
            var requestedInput = new { plcName, tableName, folderPath, name, newName, dataType, logicalAddress, externalAccessible, externalVisible, externalWritable, isSafety };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_update_tag",
                "update_tag",
                projectPath,
                target,
                requestedInput,
                () => workerClient.ListTagTablesAsync(plcName, projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.UpdateTagAsync(
                plcName,
                tableName,
                folderPath,
                name,
                newName,
                dataType,
                logicalAddress,
                externalAccessible,
                externalVisible,
                externalWritable,
                isSafety,
                projectPath).ConfigureAwait(false);
            var compileResult = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.CompileCheckAsync(null, plcName, projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("update_tag", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("update_tag", result, "compile_check", compileResult);
        }

        [McpServerTool(Name = "preview_delete_tag")]
        [Description("Preview deleting a PLC tag and return a short-lived safetyToken. Pass the token to delete_tag after reviewing the preview.")]
        public static async Task<string> PreviewDeleteTag(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table containing the tag.")] string tableName,
            [Description("Name of the tag to delete.")] string name,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            var currentState = await workerClient.ListTagTablesAsync(plcName, projectPath).ConfigureAwait(false);
            var target = new { plcName, tableName, folderPath, name };
            var requestedInput = new { plcName, tableName, folderPath, name };
            return WriteSafetyTooling.CreatePreview(
                "delete_tag",
                projectPath,
                target,
                $"Delete PLC tag '{name}' from table '{tableName}'.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "delete_tag")]
        [Description("Delete a PLC tag from an existing tag table. Requires confirm=true and a safetyToken from preview_delete_tag.")]
        public static async Task<string> DeleteTag(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table containing the tag.")] string tableName,
            [Description("Name of the tag to delete.")] string name,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_delete_tag for this exact write request.")] string? safetyToken = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with deleting a tag.";
            }

            var target = new { plcName, tableName, folderPath, name };
            var requestedInput = new { plcName, tableName, folderPath, name };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_delete_tag",
                "delete_tag",
                projectPath,
                target,
                requestedInput,
                () => workerClient.ListTagTablesAsync(plcName, projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.DeleteTagAsync(
                plcName,
                tableName,
                folderPath,
                name,
                projectPath).ConfigureAwait(false);
            var compileResult = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.CompileCheckAsync(null, plcName, projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("delete_tag", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("delete_tag", result, "compile_check", compileResult);
        }
    }

    [McpServerToolType]
    public static class UserConstantOperationsTool
    {
        [McpServerTool(Name = "preview_create_user_constant")]
        [Description("Preview creating a PLC user constant and return a short-lived safetyToken. Pass the token to create_user_constant after reviewing the preview.")]
        public static async Task<string> PreviewCreateUserConstant(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table containing the user constant.")] string tableName,
            [Description("Name of the user constant to create.")] string name,
            [Description("TIA Portal data type for the user constant.")] string dataType,
            [Description("Value for the user constant.")] string value,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            var currentState = await workerClient.ListTagTablesAsync(plcName, projectPath).ConfigureAwait(false);
            var target = new { plcName, tableName, folderPath, name };
            var requestedInput = new { plcName, tableName, folderPath, name, dataType, value };
            return WriteSafetyTooling.CreatePreview(
                "create_user_constant",
                projectPath,
                target,
                $"Create PLC user constant '{name}' in table '{tableName}'.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "create_user_constant")]
        [Description("Create a PLC user constant in an existing tag table. Requires confirm=true and a safetyToken from preview_create_user_constant.")]
        public static async Task<string> CreateUserConstant(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table containing the user constant.")] string tableName,
            [Description("Name of the user constant to create.")] string name,
            [Description("TIA Portal data type for the user constant.")] string dataType,
            [Description("Value for the user constant.")] string value,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_create_user_constant for this exact write request.")] string? safetyToken = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with creating a user constant.";
            }

            var target = new { plcName, tableName, folderPath, name };
            var requestedInput = new { plcName, tableName, folderPath, name, dataType, value };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_create_user_constant",
                "create_user_constant",
                projectPath,
                target,
                requestedInput,
                () => workerClient.ListTagTablesAsync(plcName, projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.CreateUserConstantAsync(
                plcName,
                tableName,
                folderPath,
                name,
                dataType,
                value,
                projectPath).ConfigureAwait(false);
            var compileResult = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.CompileCheckAsync(null, plcName, projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("create_user_constant", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("create_user_constant", result, "compile_check", compileResult);
        }

        [McpServerTool(Name = "preview_update_user_constant")]
        [Description("Preview updating a PLC user constant and return a short-lived safetyToken. Pass the token to update_user_constant after reviewing the preview.")]
        public static async Task<string> PreviewUpdateUserConstant(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table containing the user constant.")] string tableName,
            [Description("Name of the user constant to update.")] string name,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Optional new TIA Portal data type.")] string? dataType = null,
            [Description("Optional new value.")] string? value = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            var currentState = await workerClient.ListTagTablesAsync(plcName, projectPath).ConfigureAwait(false);
            var target = new { plcName, tableName, folderPath, name };
            var requestedInput = new { plcName, tableName, folderPath, name, dataType, value };
            return WriteSafetyTooling.CreatePreview(
                "update_user_constant",
                projectPath,
                target,
                $"Update PLC user constant '{name}' in table '{tableName}'.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "update_user_constant")]
        [Description("Update a PLC user constant data type or value. Requires confirm=true and a safetyToken from preview_update_user_constant.")]
        public static async Task<string> UpdateUserConstant(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table containing the user constant.")] string tableName,
            [Description("Name of the user constant to update.")] string name,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Optional new TIA Portal data type.")] string? dataType = null,
            [Description("Optional new value.")] string? value = null,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_update_user_constant for this exact write request.")] string? safetyToken = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with updating a user constant.";
            }

            var target = new { plcName, tableName, folderPath, name };
            var requestedInput = new { plcName, tableName, folderPath, name, dataType, value };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_update_user_constant",
                "update_user_constant",
                projectPath,
                target,
                requestedInput,
                () => workerClient.ListTagTablesAsync(plcName, projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.UpdateUserConstantAsync(
                plcName,
                tableName,
                folderPath,
                name,
                dataType,
                value,
                projectPath).ConfigureAwait(false);
            var compileResult = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.CompileCheckAsync(null, plcName, projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("update_user_constant", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("update_user_constant", result, "compile_check", compileResult);
        }

        [McpServerTool(Name = "preview_delete_user_constant")]
        [Description("Preview deleting a PLC user constant and return a short-lived safetyToken. Pass the token to delete_user_constant after reviewing the preview.")]
        public static async Task<string> PreviewDeleteUserConstant(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table containing the user constant.")] string tableName,
            [Description("Name of the user constant to delete.")] string name,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            var currentState = await workerClient.ListTagTablesAsync(plcName, projectPath).ConfigureAwait(false);
            var target = new { plcName, tableName, folderPath, name };
            var requestedInput = new { plcName, tableName, folderPath, name };
            return WriteSafetyTooling.CreatePreview(
                "delete_user_constant",
                projectPath,
                target,
                $"Delete PLC user constant '{name}' from table '{tableName}'.",
                requestedInput,
                currentState);
        }

        [McpServerTool(Name = "delete_user_constant")]
        [Description("Delete a PLC user constant from an existing tag table. Requires confirm=true and a safetyToken from preview_delete_user_constant.")]
        public static async Task<string> DeleteUserConstant(
            OpennessWorkerClient workerClient,
            [Description("Name of the tag table containing the user constant.")] string tableName,
            [Description("Name of the user constant to delete.")] string name,
            [Description("Optional PLC device name to target. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional tag table folder path, such as '/' or '/Group/Subgroup'.")] string? folderPath = null,
            [Description("Set to true to confirm the write operation. Required safety flag; operation is rejected when false.")] bool confirm = false,
            [Description("Safety token returned by preview_delete_user_constant for this exact write request.")] string? safetyToken = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            if (!confirm)
            {
                return "Operation not confirmed. Set confirm=true to proceed with deleting a user constant.";
            }

            var target = new { plcName, tableName, folderPath, name };
            var requestedInput = new { plcName, tableName, folderPath, name };
            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                "preview_delete_user_constant",
                "delete_user_constant",
                projectPath,
                target,
                requestedInput,
                () => workerClient.ListTagTablesAsync(plcName, projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.DeleteUserConstantAsync(
                plcName,
                tableName,
                folderPath,
                name,
                projectPath).ConfigureAwait(false);
            var compileResult = result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                ? null
                : await workerClient.CompileCheckAsync(null, plcName, projectPath).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("delete_user_constant", projectPath, target, requestedInput, safety.CurrentState, result);
            return WriteSafetyTooling.BuildApplyResult("delete_user_constant", result, "compile_check", compileResult);
        }
    }
}
