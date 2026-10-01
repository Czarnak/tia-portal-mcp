using System.Text.Json;
using TiaMcpServer.Contracts;
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
                $"Safety token required. Call {previewToolName} first, review the preview, then pass its safetyToken with confirm=true.",
                WorkerFailureCategories.ValidationError);
        }

        var currentState = await readCurrentState().ConfigureAwait(false);
        if (!currentState.Success)
        {
            // The pre-write state read already failed with its own real category (e.g.
            // worker_timeout / worker_crashed / postcondition_failed) — carry that through rather
            // than inventing one, so an uncertain read outcome stays an uncertain outcome.
            return WriteSafetyApplyContext.Invalid(
                $"Could not read current state before write. Error: {currentState.Error}",
                currentState.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed);
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
            ? WriteSafetyApplyContext.Valid(currentState.Payload, validation.ProjectBinding!)
            : WriteSafetyApplyContext.Invalid(
                validation.Error,
                validation.FailureCategory ?? WorkerFailureCategories.ValidationError);
    }

    public static string CreatePreview(
        WriteSafetyService safety,
        string toolName,
        string? projectPath,
        object target,
        string summary,
        object requestedInput,
        WorkerCallResult currentState,
        object? diff = null,
        string? instructions = null)
    {
        if (!currentState.Success)
        {
            return BuildApplyResult(toolName, currentState);
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
        // Failure is never rendered success-shaped: failureCategory/error/warnings are the
        // whole story, with no operationResult/verification fields implying completion.
        if (!operationResult.Success)
        {
            return JsonSerializer.Serialize(
                new
                {
                    toolName,
                    success = false,
                    failureCategory = operationResult.FailureCategory,
                    error = operationResult.Error,
                    warnings = operationResult.Warnings.Count > 0 ? operationResult.Warnings : null
                },
                TiaJson.Presentation);
        }

        return JsonSerializer.Serialize(
            new
            {
                toolName,
                success = true,
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

}

public sealed record WriteSafetyApplyContext(
    bool IsValid,
    string? Error,
    string CurrentState,
    string? FailureCategory = null,
    ProjectBindingSnapshot? ProjectBinding = null)
{
    public static WriteSafetyApplyContext Valid(
        string currentState,
        ProjectBindingSnapshot projectBinding)
    {
        return new(true, null, currentState, FailureCategory: null, ProjectBinding: projectBinding);
    }

    /// <summary>
    /// Builds an invalid apply context carrying an explicit <paramref name="failureCategory"/> from
    /// the closed <see cref="WorkerFailureCategories"/> vocabulary, so a legacy write can render
    /// a categorized failure envelope instead of a raw string.
    /// </summary>
    public static WriteSafetyApplyContext Invalid(string error, string failureCategory)
    {
        return new(false, error, string.Empty, failureCategory, ProjectBinding: null);
    }
}
