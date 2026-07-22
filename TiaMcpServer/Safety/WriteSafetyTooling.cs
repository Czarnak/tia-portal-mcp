using System.Text;
using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Safety;

public static class WriteSafetyTooling
{
    public static async Task<WriteSafetyApplyContext> ValidateForApplyAsync(
        WriteSafetyService safety,
        string? safetyToken,
        string previewToolName,
        string toolName,
        string? projectPath,
        object target,
        object requestedInput,
        Func<Task<WorkerCallResult>> readCurrentState)
    {
        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            return WriteSafetyApplyContext.Invalid(
                $"Safety token required. Call {previewToolName} first, review the preview, then pass its safetyToken with confirm=true.");
        }

        var currentState = await readCurrentState().ConfigureAwait(false);
        if (!currentState.Success)
        {
            return WriteSafetyApplyContext.Invalid(
                $"Could not read current state before write. Error: {currentState.Error}");
        }

        var validation = safety.ValidateAndConsume(
            safetyToken,
            toolName,
            projectPath,
            target,
            requestedInput,
            currentState.Payload,
            previewToolName);

        return validation.IsValid
            ? WriteSafetyApplyContext.Valid(currentState.Payload)
            : WriteSafetyApplyContext.Invalid(validation.Error);
    }

    public static string CreatePreview(
        WriteSafetyService safety,
        string toolName,
        string? projectPath,
        object target,
        string summary,
        object requestedInput,
        WorkerCallResult currentState,
        string? diff = null,
        string? instructions = null)
    {
        if (!currentState.Success)
        {
            return $"Could not read current state before preview. Error: {currentState.Error}";
        }

        return safety.CreatePreview(
            toolName,
            projectPath,
            target,
            summary,
            requestedInput,
            currentState.Payload,
            diff,
            instructions);
    }

    public static string BuildApplyResult(
        string toolName,
        WorkerCallResult operationResult,
        string? verificationName = null,
        string? verificationResult = null)
    {
        return JsonSerializer.Serialize(
            new
            {
                toolName,
                success = operationResult.Success,
                operationResult = operationResult.ToText(),
                warnings = operationResult.Warnings.Count > 0 ? operationResult.Warnings : null,
                verification = verificationName is null
                    ? null
                    : new
                    {
                        name = verificationName,
                        result = verificationResult
                    }
            },
            TiaJson.Presentation);
    }

    public static string CreateLineDiff(string before, string after)
    {
        var beforeLines = before.Replace("\r\n", "\n").Split('\n');
        var afterLines = after.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();
        builder.AppendLine("--- current");
        builder.AppendLine("+++ requested");

        var max = Math.Max(beforeLines.Length, afterLines.Length);
        for (var i = 0; i < max; i++)
        {
            var oldLine = i < beforeLines.Length ? beforeLines[i] : null;
            var newLine = i < afterLines.Length ? afterLines[i] : null;
            if (string.Equals(oldLine, newLine, StringComparison.Ordinal))
            {
                continue;
            }

            if (oldLine is not null)
            {
                builder.Append("- ").AppendLine(oldLine);
            }

            if (newLine is not null)
            {
                builder.Append("+ ").AppendLine(newLine);
            }
        }

        return builder.ToString();
    }

    public static string DescribePathState(string path)
    {
        var normalized = WriteSafetyService.NormalizeProjectPath(path);
        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            return JsonSerializer.Serialize(new
            {
                path = normalized,
                exists = true,
                length = file.Length,
                lastWriteTimeUtc = file.LastWriteTimeUtc
            }, TiaJson.Presentation);
        }

        if (Directory.Exists(path))
        {
            var directory = new DirectoryInfo(path);
            return JsonSerializer.Serialize(new
            {
                path = normalized,
                exists = true,
                isDirectory = true,
                lastWriteTimeUtc = directory.LastWriteTimeUtc
            }, TiaJson.Presentation);
        }

        return JsonSerializer.Serialize(new
        {
            path = normalized,
            exists = false
        }, TiaJson.Presentation);
    }

    public static string DescribeProjectCreationState(string projectDirectory, string projectName)
    {
        var targetDirectory = Path.Combine(projectDirectory, projectName);
        return JsonSerializer.Serialize(new
        {
            projectDirectory = WriteSafetyService.NormalizeProjectPath(projectDirectory),
            projectName,
            targetDirectory = WriteSafetyService.NormalizeProjectPath(targetDirectory),
            parentExists = Directory.Exists(projectDirectory),
            targetExists = Directory.Exists(targetDirectory)
        }, TiaJson.Presentation);
    }
}

public sealed record WriteSafetyApplyContext(bool IsValid, string? Error, string CurrentState)
{
    public static WriteSafetyApplyContext Valid(string currentState)
    {
        return new(true, null, currentState);
    }

    public static WriteSafetyApplyContext Invalid(string error)
    {
        return new(false, error, string.Empty);
    }
}
