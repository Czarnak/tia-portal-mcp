using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.ProjectLifecycle;

/// <summary>Lifecycle target resolution, consequences, worker calls and typed verification.</summary>
public sealed class LifecycleWriteDomain(OpennessWorkerClient workerClient, LifecycleWriteItem item)
    : IWriteDomain<LifecycleWriteItem, LifecycleEffects, StandaloneToolOutcome<ProjectStatusInfo>, LifecycleWriteResponse>
{
    public string ToolName => item.Operation;
    public string ContractVersion => "1.0";
    public bool ConfirmsEveryCall => true;

    public string DescribeForConfirmation(IReadOnlyList<LifecycleWriteItem> items, IReadOnlyList<ItemPlan<LifecycleEffects>> plans)
    {
        var effect = plans.Single().Effect!;
        return item.Operation switch
        {
            "open_project" => $"Confirm open_project: open '{effect.DestinationProjectPath}' and bind this session to it."
                + (effect.WillCloseSource ? $" Close source project '{effect.SourceProjectPath}'." : string.Empty),
            "create_project" => $"Confirm create_project: create '{effect.DestinationDirectory}' and bind this session to it.",
            "save_project" => $"Confirm save_project: save '{effect.SourceProjectPath}'.",
            "save_project_as" => $"Confirm save_project_as: save '{effect.SourceProjectPath}' to '{effect.DestinationDirectory}' and bind this session to the copy.",
            "archive_project" => $"Confirm archive_project: archive '{effect.SourceProjectPath}' to '{effect.ArchivePath}' with mode '{effect.ArchiveMode}'"
                + (item.SaveBeforeArchive ? ", saving first." : " without saving first."),
            "close_project" => $"Confirm close_project: close '{effect.SourceProjectPath}' and clear this session binding; "
                + (item.SaveBeforeClose ? "save before closing." : "discard unsaved changes without saving."),
            _ => ToolName
        };
    }

    public static IReadOnlyList<WriteGuardDefinition> GuardDefinitions { get; } = Array.AsReadOnly(new[]
    {
        new WriteGuardDefinition("closes_source_project", WriteGuardSeverities.Info, "A saved worker-owned source will close."),
        new WriteGuardDefinition("discards_unsaved_source_changes", WriteGuardSeverities.Block, "A modified worker-owned source must be saved or closed explicitly."),
        new WriteGuardDefinition("local_session_requires_terminal_operation", WriteGuardSeverities.Block, "A worker-opened local session requires an explicit terminal operation before another container opens."),
        new WriteGuardDefinition("local_session_source_preservation_unproved", WriteGuardSeverities.Block, "Preservation of the already-open local session during another open is unproved."),
        new WriteGuardDefinition("discards_unsaved_changes", WriteGuardSeverities.Acknowledge, "Closing without saving discards unsaved changes."),
        new WriteGuardDefinition("archive_without_save", WriteGuardSeverities.Info, "The modified project will be archived without saving."),
        new WriteGuardDefinition("archive_discards_restorable_data", WriteGuardSeverities.Info, "The archive omits restorable data."),
        new WriteGuardDefinition("archive_inside_project_folder", WriteGuardSeverities.Block, "An archive cannot be written inside the project folder."),
        new WriteGuardDefinition("target_exists", WriteGuardSeverities.Block, "The destination directory already exists.")
    });

    public static WriteGuardCatalog Catalog { get; } = new(GuardDefinitions);

    private LifecycleEffects? _planned;
    private StandaloneToolOutcome<ProjectLifecycleResultInfo>? _mutation;
    private IReadOnlyList<string> _verificationWarnings = Array.Empty<string>();
    private const string InspectBeforeRetry = "The lifecycle mutation may already have completed. Inspect the current project and filesystem before retrying; no rollback or replay was attempted.";

    public WriteValidation Validate(IReadOnlyList<LifecycleWriteItem> items, McpAccessMode accessMode)
    {
        if (accessMode == McpAccessMode.ReadOnly)
            return WriteValidation.Invalid(WorkerFailureCategories.AccessDenied, "Lifecycle writes require read-write or full access mode.");
        if (items.Count != 1 || items[0].Operation != ToolName)
            return Invalid("Exactly one host-supplied lifecycle operation is required.");
        try
        {
            if (item.ProjectPath is not null && !Path.IsPathFullyQualified(item.ProjectPath))
                return Invalid("ProjectPath must be an absolute path.");
            switch (ToolName)
            {
                case "open_project":
                    if (string.IsNullOrWhiteSpace(item.ProjectPath)) return Invalid("Project path is required.");
                    if (Path.GetExtension(item.ProjectPath) is { } extension
                        && !string.Equals(extension, ".ap21", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(extension, ".als21", StringComparison.OrdinalIgnoreCase))
                        return Invalid("Only an existing .ap21 or .als21 file can be opened.");
                    if (!File.Exists(item.ProjectPath))
                        return Invalid("The destination project file must exist.");
                    break;
                case "create_project":
                    if (!ValidDirectory(item.ProjectDirectory) || !ValidName(item.ProjectName))
                        return Invalid("ProjectDirectory must exist and ProjectName must be a valid directory name.");
                    break;
                case "save_project_as":
                    if (!item.Rebind) return Invalid("save_project_as with rebind=false is not supported. Use rebind=true.");
                    if (!ValidDirectory(item.TargetDirectory) || !ValidName(item.TargetName))
                        return Invalid("TargetDirectory must exist and TargetName must be a valid directory name.");
                    break;
                case "archive_project":
                    if (!ValidDirectory(item.ArchiveDirectory) || !ValidName(item.ArchiveName))
                        return Invalid("The archive directory must already exist and ArchiveName must be a valid file name.");
                    if (!ArchiveModeNames.TryNormalize(item.Mode, out _, out var error)) return Invalid(error!);
                    break;
                case "save_project":
                case "close_project":
                    break;
                default:
                    return Invalid("Unknown lifecycle operation.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Invalid("A lifecycle filesystem path is invalid.");
        }
        return WriteValidation.Valid();
    }

    public async Task<WritePlan<LifecycleEffects>> PlanAsync(string? projectPath, IReadOnlyList<LifecycleWriteItem> items)
    {
        var binding = workerClient.BindingSnapshot;
        var sourcePath = ProjectPathNormalization.Canonicalize(binding.ProjectPath);
        ProjectStatusInfo? sourceStatus = null;
        bool? sourceOwned = null;
        var closesSource = false;
        ProjectContextInfo? sourceContext = binding.Context;
        string? destinationPath = item.Operation == "open_project" ? ProjectPathNormalization.Canonicalize(item.ProjectPath) : null;
        string? destinationDirectory = item.Operation switch
        {
            "create_project" => Path.GetFullPath(Path.Combine(item.ProjectDirectory!, item.ProjectName!)),
            "save_project_as" => Path.GetFullPath(Path.Combine(item.TargetDirectory!, item.TargetName!)),
            _ => null
        };

        if (binding.IsVerified && item.Operation == "open_project" && !LifecyclePayloadContract.SamePath(sourcePath, destinationPath))
        {
            var probe = await workerClient.ProbeOpenProjectRebindAsync(sourcePath!, destinationPath!).ConfigureAwait(false);
            if (!probe.Success) return FailedPlan(probe);
            try
            {
                var rebind = ProjectRebindStatePayloadContract.Decode(probe.Payload, sourcePath!, destinationPath!);
                sourceOwned = rebind.SourceOpenedByWorker;
                closesSource = rebind.WillCloseSource;
                sourceContext = rebind.SourceContext;
                // The rebind probe is the authoritative saved-state/ownership observation.
                sourceStatus = new ProjectStatusInfo { IsOpen = true, Path = sourcePath,
                    IsModified = rebind.SourceIsModified, Context = sourceContext };
            }
            catch (JsonException) { return ProtocolPlan(); }
        }
        else if (binding.IsVerified)
        {
            var probe = await workerClient.ProbeProjectStatusForLifecycleAsync(sourcePath).ConfigureAwait(false);
            if (!probe.Success) return FailedPlan(probe);
            try
            {
                sourceStatus = LifecyclePayloadContract.DecodeStatus(probe, "probe_project_status_for_lifecycle", sourcePath, expectOpen: true);
                sourceContext = sourceStatus.Context;
                if ((item.Operation == "close_project" && !item.SaveBeforeClose
                    || item.Operation == "archive_project" && !item.SaveBeforeArchive) && sourceStatus.IsModified is null)
                    return WritePlan<LifecycleEffects>.Fail(WorkerFailureCategories.ProtocolError,
                        "The lifecycle probe did not establish whether the source has unsaved changes.");
            }
            catch (JsonException) { return ProtocolPlan(); }
        }

        ArchiveModeNames.TryNormalize(item.Mode, out var archiveMode, out _);
        var effect = new LifecycleEffects(sourcePath, destinationPath, destinationDirectory, sourceStatus,
            sourceOwned, closesSource,
            item.Operation == "save_project" || item.Operation == "close_project" && item.SaveBeforeClose
                || item.Operation == "archive_project" && item.SaveBeforeArchive,
            item.Operation is "open_project" or "create_project" or "save_project_as",
            destinationDirectory is not null && (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory)),
            item.Operation == "archive_project" ? archiveMode : null,
            item.Operation == "archive_project" ? Path.GetFullPath(Path.Combine(item.ArchiveDirectory!,
                ArchiveModeNames.EnsureArchiveExtension(item.ArchiveName!, archiveMode))) : null,
            sourceContext);
        _planned = effect;
        return WritePlan<LifecycleEffects>.Ok(new[] { ItemPlan<LifecycleEffects>.Resolved(effect) });
    }

    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<LifecycleWriteItem> items, IReadOnlyList<ItemPlan<LifecycleEffects>> plans)
    {
        var effect = plans.Single().Effect!;
        var guards = new List<FiredGuard>();
        void Fire(string id, string message) => guards.Add(new(id, item.OperationId, message));
        if (item.Operation == "open_project" && effect.SourceContext is not null
            && !LifecyclePayloadContract.SamePath(effect.SourceContext.SessionContainerPath,
                effect.DestinationProjectPath))
        {
            if (effect.SourceContext.OpenedByWorker)
                Fire("local_session_requires_terminal_operation",
                    "The worker-opened local session requires an explicit terminal operation before another container opens.");
            else
                Fire("local_session_source_preservation_unproved",
                    "The already-open local session cannot be proved preserved during another project or session open.");
        }
        if (effect.WillCloseSource)
        {
            if (effect.SourceStatus?.IsModified == true)
                Fire("discards_unsaved_source_changes", $"Worker-owned source '{effect.SourceProjectPath}' has unsaved changes and would close. Save or close it explicitly first.");
            else
                Fire("closes_source_project", $"Worker-owned saved source '{effect.SourceProjectPath}' will close when '{effect.DestinationProjectPath}' opens.");
        }
        if (item.Operation == "close_project" && !item.SaveBeforeClose && effect.SourceStatus?.IsModified == true)
            Fire("discards_unsaved_changes", $"Close '{effect.SourceProjectPath}' without saving and discard its unsaved changes.");
        if (item.Operation == "archive_project")
        {
            if (!item.SaveBeforeArchive && effect.SourceStatus?.IsModified == true)
                Fire("archive_without_save", $"Archive modified project '{effect.SourceProjectPath}' to '{effect.ArchivePath}' without saving first.");
            if (effect.ArchiveMode is ArchiveModeNames.DiscardRestorableData or ArchiveModeNames.DiscardRestorableDataAndCompressed)
                Fire("archive_discards_restorable_data", $"Archive '{effect.ArchivePath}' omits restorable data.");
            if (ArchiveDirectoryGuard.IsWithinProjectFolder(item.ArchiveDirectory!, effect.SourceProjectPath ?? string.Empty))
                Fire("archive_inside_project_folder", ArchiveDirectoryGuard.BuildRejectionMessage(item.ArchiveDirectory!));
        }
        if (effect.TargetExists)
            Fire("target_exists", $"Destination '{effect.DestinationDirectory}' already exists.");
        return guards;
    }

    public async Task<ItemReplan<LifecycleEffects>> ReplanAsync(string? projectPath, LifecycleWriteItem requestedItem)
    {
        var plan = await PlanAsync(projectPath, new[] { requestedItem }).ConfigureAwait(false);
        return plan.Success ? ItemReplan<LifecycleEffects>.Ok(plan.Items.Single())
            : ItemReplan<LifecycleEffects>.Fail(plan.Error!.Category, plan.Error.Message);
    }

    public Task<WorkerCallResult> MutateAsync(string? projectPath, LifecycleWriteItem requestedItem) => item.Operation switch
    {
        "open_project" => workerClient.OpenProjectAsync(item.ProjectPath!, item.ForceRebind),
        "create_project" => workerClient.CreateProjectAsync(item.ProjectDirectory!, item.ProjectName!, item.Author, item.Comment),
        "save_project" => workerClient.SaveProjectAsync(item.ProjectPath),
        "save_project_as" => workerClient.SaveProjectAsAsync(item.ProjectPath, item.TargetDirectory!, item.TargetName!, item.Rebind),
        "archive_project" => workerClient.ArchiveProjectAsync(item.ProjectPath, item.ArchiveDirectory!, item.ArchiveName!, item.Mode, item.SaveBeforeArchive),
        "close_project" => workerClient.CloseProjectAsync(item.ProjectPath, item.SaveBeforeClose),
        _ => throw new InvalidOperationException("Unknown lifecycle operation.")
    };

    public StructuredOperationItem Project(LifecycleWriteItem requestedItem, WorkerCallResult result)
    {
        if (!result.Success) _mutation = LifecyclePayloadContract.Failed<ProjectLifecycleResultInfo>(result);
        else
        {
            try
            {
                var expected = item.Operation switch
                {
                    "open_project" when string.Equals(Path.GetExtension(_planned!.DestinationProjectPath),
                        ".als21", StringComparison.OrdinalIgnoreCase) => null,
                    "open_project" => _planned!.DestinationProjectPath,
                    "save_project" or "archive_project" or "close_project" => _planned!.SourceProjectPath,
                    _ => workerClient.BindingSnapshot.ProjectPath
                };
                var payload = LifecyclePayloadContract.Decode(result, item.Operation, expected, _planned!.DestinationDirectory);
                if (item.Operation == "open_project" && string.Equals(Path.GetExtension(item.ProjectPath),
                        ".als21", StringComparison.OrdinalIgnoreCase)
                    && (!LifecyclePayloadContract.SamePath(payload.Project?.Context?.SessionContainerPath, item.ProjectPath)
                        || payload.Project?.Context?.OpenedByWorker != true))
                    throw new JsonException(LifecyclePayloadContract.ProtocolFailureMessage);
                _mutation = new(OperationBatchStatus.Succeeded, payload, null, null);
            }
            catch (JsonException) { _mutation = LifecyclePayloadContract.ProtocolFailure<ProjectLifecycleResultInfo>(); }
        }
        return new(item.OperationId, item.Operation, _mutation!.Status,
            _mutation.Value is null ? null : CanonicalJson.ToElement(_mutation.Value),
            _mutation.Failure, null, null, result.Warnings);
    }

    public async Task<StandaloneToolOutcome<ProjectStatusInfo>?> VerifyAsync(string? projectPath, StructuredOperationBatch batch)
    {
        if (_mutation?.Status != OperationBatchStatus.Succeeded) return null;
        var closed = item.Operation == "close_project";
        var expected = closed ? null : _mutation.Value!.ProjectPath;
        var result = await workerClient.GetBasicProjectStatusAsync(expected).ConfigureAwait(false);
        _verificationWarnings = result.Warnings;
        if (!result.Success) return LifecyclePayloadContract.Failed<ProjectStatusInfo>(result);
        try
        {
            var status = LifecyclePayloadContract.DecodeStatus(result, "get_project_status", expected, !closed);
            if (!closed)
            {
                ProjectContextPayloadContract.ValidateStableOwner(_mutation.Value!.Project?.Context, status.Context);
                ProjectContextPayloadContract.ValidateStableOwner(workerClient.BindingSnapshot.Context, status.Context);
            }
            if (item.Operation == "save_project" && status.IsModified != false)
                return new(OperationBatchStatus.Failed, status,
                    new(WorkerFailureCategories.PostconditionFailed, "The project was not verified as saved."), null);
            return new(OperationBatchStatus.Succeeded, status, null, null);
        }
        catch (JsonException)
        {
            if (_mutation.Value?.Project?.Context?.ContainerKind == ProjectContainerKinds.LocalSession)
                workerClient.InvalidateRejectedLifecycleContext();
            return LifecyclePayloadContract.ProtocolFailure<ProjectStatusInfo>();
        }
    }

    public bool VerificationSucceeded(StandaloneToolOutcome<ProjectStatusInfo>? verification)
        => verification?.Status == OperationBatchStatus.Succeeded;

    public LifecycleWriteResponse Compose(WriteReport<LifecycleEffects, StandaloneToolOutcome<ProjectStatusInfo>> report)
    {
        var result = _mutation;
        var verification = report.Verification;
        var warnings = report.Warnings.Concat(report.Batch?.Operations.SelectMany(operation => operation.Warnings)
            ?? Array.Empty<string>()).Concat(_verificationWarnings).Distinct(StringComparer.Ordinal).ToList();
        if (report.Phase == WritePhases.Applied && (result?.Status != OperationBatchStatus.Succeeded
            || verification?.Status != OperationBatchStatus.Succeeded)) warnings.Add(InspectBeforeRetry);
        var effect = report.Effects.FirstOrDefault()?.Effect;
        result = BoundOutcome(result);
        verification = BoundOutcome(verification);
        // Response omission changes presentation; the execution and verification verdict stays true.
        var response = new LifecycleWriteResponse(ToolName, ContractVersion, report.Success, report.Error,
            warnings, report.Phase, report.Guards, effect, result, verification);
        if (effect?.SourceStatus is not null
            && CanonicalJson.Serialize(effect.SourceStatus).Length > StructuredOperationBatchPayloadBudget.MaxItemChars)
        {
            effect = effect with { SourceStatus = null };
            warnings.Add("The complete source status was omitted to fit the lifecycle response budget.");
            response = response with { Effects = effect };
        }
        if (CanonicalJson.Serialize(response).Length > StructuredOperationBatchPayloadBudget.MaxDocumentChars && result?.Value is not null)
        {
            result = OmitOutcome(result, StructuredOperationBatchPayloadBudget.DocumentLimitReason,
                StructuredOperationBatchPayloadBudget.MaxDocumentChars);
            response = response with { Result = result };
        }
        if (CanonicalJson.Serialize(response).Length > StructuredOperationBatchPayloadBudget.MaxDocumentChars && verification?.Value is not null)
        {
            verification = OmitOutcome(verification, StructuredOperationBatchPayloadBudget.DocumentLimitReason,
                StructuredOperationBatchPayloadBudget.MaxDocumentChars);
            response = response with { Verification = verification };
        }
        var omittedWarnings = 0;
        while (CanonicalJson.Serialize(response).Length > StructuredOperationBatchPayloadBudget.MaxDocumentChars && warnings.Count > 0)
        {
            // Drop whole warning entries, largest first, preserving useful short diagnostics.
            var largest = warnings.Select((warning, index) => (warning.Length, index)).MaxBy(entry => entry.Length);
            warnings.RemoveAt(largest.index);
            omittedWarnings++;
        }
        if (omittedWarnings > 0)
        {
            warnings.Add($"{omittedWarnings} warning entries were omitted to fit the lifecycle response budget.");
            while (CanonicalJson.Serialize(response).Length > StructuredOperationBatchPayloadBudget.MaxDocumentChars && warnings.Count > 1)
            {
                var largest = warnings.Take(warnings.Count - 1).Select((warning, index) => (warning.Length, index)).MaxBy(entry => entry.Length);
                warnings.RemoveAt(largest.index);
                warnings[^1] = $"{++omittedWarnings} warning entries were omitted to fit the lifecycle response budget.";
            }
        }
        if (CanonicalJson.Serialize(response).Length > StructuredOperationBatchPayloadBudget.MaxDocumentChars)
        {
            static StructuredOperationFailure? Shorten(StructuredOperationFailure? failure) => failure is null || failure.Message.Length <= 120 ? failure
                : failure with { Message = failure.Message[..Math.Min(120, failure.Message.Length)] + " [message shortened]" };
            result = result is null ? null : result with { Failure = Shorten(result.Failure) };
            verification = verification is null ? null : verification with { Failure = Shorten(verification.Failure) };
            response = response with
            {
                Result = result, Verification = verification,
                Error = response.Error is null || response.Error.Message.Length <= 120 ? response.Error : response.Error with
                    { Message = response.Error.Message[..Math.Min(120, response.Error.Message.Length)] + " [message shortened]" }
            };
        }
        // Preview and blocked calls have no outcomes to omit. Repeated path evidence can still
        // exceed the document limit, so discard complete evidence instead of truncating identities.
        if (CanonicalJson.Serialize(response).Length > StructuredOperationBatchPayloadBudget.MaxDocumentChars
            && response.Effects?.SourceStatus is not null)
        {
            response = response with { Effects = response.Effects with { SourceStatus = null } };
            warnings.Add("The complete source status was omitted to fit the lifecycle response budget.");
        }
        if (CanonicalJson.Serialize(response).Length > StructuredOperationBatchPayloadBudget.MaxDocumentChars
            && response.Effects is not null)
        {
            response = response with { Effects = null };
            warnings.Add("The complete lifecycle effects were omitted to fit the lifecycle response budget. Inspect the current project and filesystem before retrying.");
        }
        if (CanonicalJson.Serialize(response).Length > StructuredOperationBatchPayloadBudget.MaxDocumentChars)
        {
            var guards = response.Guards.ToArray();
            warnings.Add("Complete detailed guard messages were omitted to fit the lifecycle response budget; guard ids, severity, and acknowledgement states are retained.");
            foreach (var index in Enumerable.Range(0, guards.Length).OrderByDescending(index => guards[index].Message.Length))
            {
                if (CanonicalJson.Serialize(response).Length <= StructuredOperationBatchPayloadBudget.MaxDocumentChars) break;
                var guard = guards[index];
                guards[index] = guard with
                {
                    Message = Catalog.Get(guard.Id).Description + " [Detailed guard message omitted to fit the lifecycle response budget.]"
                };
                response = response with { Guards = guards };
            }
        }
        return response;
    }

    private StandaloneToolOutcome<T>? BoundOutcome<T>(StandaloneToolOutcome<T>? outcome) where T : class
    {
        if (outcome?.Value is null) return outcome;
        var length = CanonicalJson.Serialize(outcome.Value).Length;
        return length <= StructuredOperationBatchPayloadBudget.MaxItemChars ? outcome
            : OmitOutcome(outcome, StructuredOperationBatchPayloadBudget.ItemLimitReason, StructuredOperationBatchPayloadBudget.MaxItemChars);
    }

    private static StandaloneToolOutcome<T> OmitOutcome<T>(StandaloneToolOutcome<T> outcome, string reason, int limit) where T : class
        => outcome with
        {
            Status = OperationBatchStatus.Omitted, Value = null,
            Omission = new(reason, limit, CanonicalJson.Serialize(outcome.Value).Length, "get_project_status",
                "The complete lifecycle value was omitted. Read current project status; inspect filesystem artifacts before deciding whether to retry the mutation.")
        };

    private static bool ValidDirectory(string? path) => path is not null && Path.IsPathFullyQualified(path) && Directory.Exists(path);
    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name is not "." and not ".."
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith(' ') && !name.EndsWith('.');
    private static WriteValidation Invalid(string message) => WriteValidation.Invalid(WorkerFailureCategories.ValidationError, message);
    private static WritePlan<LifecycleEffects> FailedPlan(WorkerCallResult result) => WritePlan<LifecycleEffects>.Fail(
        result.FailureCategory ?? WorkerFailureCategories.ProtocolError, result.Error ?? "The lifecycle state probe failed.");
    private static WritePlan<LifecycleEffects> ProtocolPlan() => WritePlan<LifecycleEffects>.Fail(
        WorkerFailureCategories.ProtocolError, LifecyclePayloadContract.ProtocolFailureMessage);
}
