using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using static TiaMcpServer.FakeWorker.ScenarioSupport;

namespace TiaMcpServer.FakeWorker.Lifecycle;

internal sealed class LifecycleScenarios
{
    private int lifecycleRebindProbeReadCount = 0;
    private int lifecycleRebindOpenProjectCalls = 0;
    private string? guardedLifecycleScenario = null;
    public string? ProbeOnlyCopiedPath { get; private set; }
    private bool guardedLifecycleModified = false;

    public bool FailClosedVerification
        => guardedLifecycleScenario?.Contains("-verification-failure", StringComparison.OrdinalIgnoreCase) == true;

    public void RecordOpenProject() => lifecycleRebindOpenProjectCalls++;

    public bool TryHandleGuarded(string line, string? currentProjectPath, string? fakeProjectPath, out string response)
    {
        if (currentProjectPath?.Contains("guarded-lifecycle", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (!string.Equals(guardedLifecycleScenario, currentProjectPath, StringComparison.OrdinalIgnoreCase))
                guardedLifecycleModified = currentProjectPath.Contains("-modified", StringComparison.OrdinalIgnoreCase);
            guardedLifecycleScenario = currentProjectPath;
        }
        if (guardedLifecycleScenario is not null && (currentProjectPath?.Contains("guarded-lifecycle", StringComparison.OrdinalIgnoreCase) == true
            || currentProjectPath is null && ReadMethod(line) == "get_basic_project_status"))
        {
            response = GuardedLifecycleResponse(line, guardedLifecycleScenario, currentProjectPath, fakeProjectPath);
            return true;
        }

        response = string.Empty;
        return false;
    }

    public void Handle(string line, string scenario, string? currentProjectPath,
        WorkerSessionIdentity? currentExpectedSessionIdentity, Action<string, bool> respond)
    {
        void Respond(string json, bool includeSessionIdentity = true) => respond(json, includeSessionIdentity);
        switch (scenario)
        {
            case "direct-status-only":
                // Used to prove the direct get_project_status MCP tool routes through the
                // GetProjectStatusAsync operation only, never the internal lifecycle probe.
                Respond(ReadMethod(line) == "get_project_status"
                    ? Success(DirectStatusPayload(new ProjectStatusInfo { IsOpen = true, Path = currentProjectPath }))
                    : $$"""{"success":false,"error":"expected get_project_status, got '{{ReadMethod(line)}}'"}""");
                break;
            case "status-no-project":
                // Simulates the real worker's GetStatusReadOnly when nothing is open and no path
                // was requested: isOpen:false, no resolvedProjectPath - nothing was opened.
                Respond(Success(WorkerJson.SerializePayload(new ProjectStatusResultInfo
                {
                    Operation = "get_project_status",
                    Project = new ProjectStatusInfo { IsOpen = false }
                })));
                break;
            case "status-with-metadata":
                // Simulates the real worker's GetStatusReadOnly WITH the extended metadata surface,
                // exercising every collection/section of ProjectMetadataInfo so host-side contract
                // tests can assert the full additive metadata schema over the real IPC pipe. Built
                // from the shared Contracts DTO so a contract change here is a compile error, never
                // a silently stale hand-written literal.
                Respond(ReadMethod(line) == "get_project_status"
                    ? Success(DirectStatusPayload(StatusWithMetadataFixture()))
                    : $$"""{"success":false,"error":"expected get_project_status, got '{{ReadMethod(line)}}'"}""");
                break;
            case "status-malformed":
                Respond(Success("{\"PRIVATE_STATUS_MARKER\":\"" + new string('x', 70000) + "\"}"));
                break;
            case "status-oversized":
            {
                var status = StatusWithMetadataFixture();
                status.Metadata!.Comment!.Translations![0].Text = new string('x', 70_000);
                var oversizedPayload = DirectStatusPayload(status);
                Respond(ReadMethod(line) == "get_project_status"
                    ? Success(oversizedPayload)
                    : $$"""{"success":false,"error":"expected get_project_status, got '{{ReadMethod(line)}}'"}""");
                break;
            }
            case "lifecycle-probe-only":
                // Guards against a regression where a save/save-as/archive/close current-state
                // read reverts to the direct status operation. Only the unbound direct-status fixture
                // bootstrap succeeds; a bound direct status still fails. Every other operation (the
                // probe itself, or the tool's own write call that follows) succeeds normally so the
                // full preview/apply round trip can complete. save_project_as additionally needs a
                // resolvedProjectPath so the rebind bind succeeds after the write.
                Respond(ReadMethod(line) switch
                {
                    "get_project_status" when currentExpectedSessionIdentity is null =>
                        """{"success":true,"payload":"{\"isOpen\":true}"}""",
                    "get_project_status" =>
                        """{"success":false,"error":"current-state read must use probe_project_status_for_lifecycle, not get_project_status"}""",
                    "save_project_as" => """{"success":true,"payload":"{\"isOpen\":true}","resolvedProjectPath":"C:\\lifecycle\\Copy.ap21"}""",
                    _ => """{"success":true,"payload":"{\"isOpen\":true}"}"""
                });
                break;
            case @"C:\FakeWorker\lifecycle-rebind-probe.ap21":
            case @"C:\FakeWorker\lifecycle-rebind-probe-missing-source.ap21":
            case @"C:\FakeWorker\lifecycle-rebind-probe-null-modified.ap21":
            case @"C:\FakeWorker\lifecycle-rebind-probe-wrong-destination.ap21":
            case @"C:\FakeWorker\lifecycle-rebind-probe-wrong-disposition.ap21":
            case @"C:\FakeWorker\lifecycle-rebind-probe-binding-conflict.ap21":
                // One persistent process first verifies source A, then handles the internal probe.
                // The destination travels only in rebindDestinationProjectPath, never projectPath.
                if (ReadMethod(line) == "get_project_status")
                {
                    Respond(Success(WorkerJson.SerializePayload(new { isOpen = true, openProjectCalls = lifecycleRebindOpenProjectCalls })));
                }
                else if (ReadMethod(line) == "get_basic_project_status")
                {
                    Respond(Success("{\"isOpen\":true}"));
                }
                else if (ReadMethod(line) == "probe_project_status_for_lifecycle")
                {
                    Respond(Success("{\"isOpen\":true}"));
                }
                else if (ReadMethod(line) == "open_project")
                {
                    RecordOpenProject();
                    Respond(SuccessWithResolvedPath("{\"isOpen\":true}", currentProjectPath!));
                }
                else
                {
                    Respond(RebindProbeResponse(line, currentProjectPath!));
                }
                break;
            case "save-as-uncertain-state":
                // Simulates the real worker's postcondition_failed when save_project_as saved a copy
                // but could not confirm the active project is that copy: a failure carrying the
                // uncertain-state warning. The unbound status bootstrap and the protected lifecycle
                // probe succeed so a registered save-as dry run can proceed; only the
                // protected save-as apply returns the failure and retains its verified source binding.
                Respond(ReadMethod(line) is "probe_project_status_for_lifecycle" ||
                        (ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null)
                    ? """{"success":true,"payload":"{\"isOpen\":true}"}"""
                    : """{"success":false,"failureCategory":"postcondition_failed","error":"could not confirm the copied project path","warnings":["Project state may have changed; inspect the open project before retrying."]}""");
                break;
            case "C:\\bound\\FailingSave.ap21":
                // A bound-path scenario whose save_project_as call fails, proving a failed rebinding
                // save-as leaves the pre-existing session binding untouched (no partial rebind). Its
                // unbound status call exists only to establish that exact verified fixture binding.
                Respond(ReadMethod(line) == "get_project_status" &&
                        currentExpectedSessionIdentity is null
                    ? """{"success":true,"payload":"{\"isOpen\":true}"}"""
                    : """{"success":false,"failureCategory":"worker_operation_failed","error":"save failed"}""");
                break;
            case "C:\\Projects\\SimpleProject\\SimpleProject.ap21":
                // Used by the archive-directory-guard preview test: every request (including the
                // probe_project_status_for_lifecycle current-state read) reports itself as the
                // resolvedProjectPath, so the host-side ArchiveDirectoryGuard check has a concrete
                // project path to classify the caller's archiveDirectory against.
                Respond("""{"success":true,"payload":"{\"isOpen\":true}","resolvedProjectPath":"C:\\Projects\\SimpleProject\\SimpleProject.ap21"}""");
                break;

        }
    }

    public void UpgradeLegacyFixture(JsonObject response, string? currentMethod, string? currentProjectPath,
        string? fakeProjectPath, string? currentRequestLine, Func<string?, string?> scenarioKey)
    {
        if (response["success"]?.GetValue<bool>() != true
            || currentMethod is not ("open_project" or "create_project" or "save_project" or "save_project_as"
                or "archive_project" or "close_project" or "probe_project_status_for_lifecycle" or "get_basic_project_status")) return;
        var payload = response["payload"]?.GetValue<string>();
        if (payload is null || JsonNode.Parse(payload) is not JsonObject root
            || root.Any(property => property.Key != "isOpen")) return;
        var path = ProjectPathNormalization.Canonicalize(response["resolvedProjectPath"]?.GetValue<string>() ?? currentProjectPath ?? fakeProjectPath);
        if (currentMethod == "save_project_as" && scenarioKey(currentProjectPath) == "lifecycle-probe-only")
        {
            var directory = Path.Combine(ReadField(currentRequestLine!, "targetDirectory")!, ReadField(currentRequestLine!, "targetName")!);
            path = Path.Combine(directory, "Copy.ap21");
            response["resolvedProjectPath"] = path;
            ProbeOnlyCopiedPath = path;
        }
        var status = new ProjectStatusInfo
        {
            IsOpen = currentMethod != "close_project", Path = path,
            IsModified = currentMethod == "close_project" ? null : false
        };
        response["payload"] = WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
        {
            Operation = currentMethod == "get_basic_project_status" ? "get_project_status" : currentMethod!,
            ProjectPath = path, Project = status
        });
        response["resolvedProjectPath"] = currentMethod == "close_project" ? null : path;
    }

    private string GuardedLifecycleResponse(string requestLine, string fixture, string? currentProjectPath, string? fakeProjectPath)
    {
        var method = ReadMethod(requestLine);
        var mutation = method is "open_project" or "create_project" or "save_project" or "save_project_as" or "archive_project" or "close_project";
        if (mutation && fixture.Contains("-worker-failure", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.WorkerOperationFailed, Error = "Scripted lifecycle mutation failure." }, WorkerJson.Envelope);
        var path = ProjectPathNormalization.Canonicalize(currentProjectPath ?? fakeProjectPath);
        if (method == "probe_open_project_rebind")
            return Success(WorkerJson.SerializePayload(ProjectRebindStateInfo.Create(fakeProjectPath,
                ReadField(requestLine, "rebindDestinationProjectPath")!,
                fakeProjectPath is null ? null : guardedLifecycleModified,
                fakeProjectPath is not null && fixture.Contains("-ui-owned", StringComparison.OrdinalIgnoreCase) != true)));
        if (method == "create_project")
        {
            var directory = Path.Combine(ReadField(requestLine, "projectDirectory")!, ReadField(requestLine, "projectName")!);
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, ReadField(requestLine, "projectName")! + ".ap21");
            File.WriteAllText(path, "FakeWorker lifecycle artifact");
        }
        if (method == "save_project_as")
        {
            var directory = Path.Combine(ReadField(requestLine, "targetDirectory")!, ReadField(requestLine, "targetName")!);
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, Path.GetFileName(fakeProjectPath ?? "Project.ap21"));
            File.WriteAllText(path, "FakeWorker copied lifecycle artifact");
        }
        if (method is "save_project" or "save_project_as" or "create_project") guardedLifecycleModified = false;
        if (method == "archive_project")
        {
            ArchiveModeNames.TryNormalize(ReadField(requestLine, "archiveMode"), out var archiveMode, out _);
            var name = ArchiveModeNames.EnsureArchiveExtension(ReadField(requestLine, "archiveName")!, archiveMode);
            var target = Path.Combine(ReadField(requestLine, "archiveDirectory")!, name);
            if (archiveMode is ArchiveModeNames.Compressed or ArchiveModeNames.DiscardRestorableDataAndCompressed)
                File.WriteAllText(target, "FakeWorker archived lifecycle artifact");
            else Directory.CreateDirectory(target);
            using var request = JsonDocument.Parse(requestLine);
            if (request.RootElement.TryGetProperty("saveBeforeArchive", out var save) && save.ValueKind == JsonValueKind.True)
                guardedLifecycleModified = false;
        }
        var noProject = method == "get_basic_project_status" && fakeProjectPath is null;
        var status = new ProjectStatusInfo
        {
            IsOpen = method != "close_project" && !noProject,
            Path = noProject ? null : path,
            Name = noProject ? null : Path.GetFileNameWithoutExtension(path),
            IsModified = noProject || method == "close_project" ? null : guardedLifecycleModified,
            Version = noProject ? null : "V21",
            Author = fixture.Contains("-oversized", StringComparison.OrdinalIgnoreCase) ? new string('x', 70_000) : null
        };
        if (method == "get_project_status") return Success(DirectStatusPayload(status));
        var payload = WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
        {
            Operation = method == "get_basic_project_status" ? "get_project_status" : method!,
            ProjectPath = status.Path, Project = status
        });
        if (fixture.Contains("-document-limit", StringComparison.OrdinalIgnoreCase))
        {
            // Each outcome fits individually; three copies plus the response envelope exceed its
            // document budget. This exercises omission of a complete value after composition.
            status.Author = new string('x', Math.Max(0, 59_999 - payload.Length));
            payload = WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
            {
                Operation = method == "get_basic_project_status" ? "get_project_status" : method!,
                ProjectPath = status.Path, Project = status
            });
        }
        if (mutation && fixture.Contains("-malformed", StringComparison.OrdinalIgnoreCase)
            || method == "get_basic_project_status" && fixture.Contains("-verification-failure", StringComparison.OrdinalIgnoreCase))
            payload = "{\"untrustedMarker\":true}";
        return JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload,
            ResolvedProjectPath = method == "close_project" || noProject ? null : path,
            Warnings = mutation && fixture.Contains("-oversized", StringComparison.OrdinalIgnoreCase)
                ? new List<string> { new string('w', 190_000), "retained mutation warning" } : new List<string>() }, WorkerJson.Envelope);
    }

    string RebindProbeResponse(string requestLine, string sourcePath)
    {
        const string destinationPath = @"C:\Lifecycle\B.ap21";
        const string modifiedDestinationPath = @"C:\Lifecycle\B-modified.ap21";
        const string uiOwnedDestinationPath = @"C:\Lifecycle\B-ui-owned.ap21";
        const string driftDestinationPath = @"C:\open\Line.ap21";
        var requestedDestination = ReadField(requestLine, "rebindDestinationProjectPath");
        var destinationName = Path.GetFileName(requestedDestination);
        var knownDestination = destinationName is "B.ap21" or "B-modified.ap21" or "B-ui-owned.ap21" or "Line.ap21" ||
            string.Equals(requestedDestination, destinationPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestedDestination, modifiedDestinationPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestedDestination, uiOwnedDestinationPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestedDestination, driftDestinationPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(requestedDestination, sourcePath, StringComparison.OrdinalIgnoreCase);
        if (ReadMethod(requestLine) != "probe_open_project_rebind" ||
            !string.Equals(ReadField(requestLine, "projectPath"), sourcePath, StringComparison.OrdinalIgnoreCase) ||
            !knownDestination)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                failureCategory = WorkerFailureCategories.ValidationError,
                error = "The rebind probe request did not carry the exact source and destination."
            });
        }

        if (sourcePath.EndsWith("-binding-conflict.ap21", StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                failureCategory = WorkerFailureCategories.BindingConflict,
                error = "The source session changed before the rebind-state read."
            });
        }

        var isDrift = requestedDestination?.EndsWith(@"\open\Line.ap21", StringComparison.OrdinalIgnoreCase) == true;
        var isModified = destinationName is "B-modified.ap21" or "B-ui-owned.ap21" ||
            string.Equals(requestedDestination, sourcePath, StringComparison.OrdinalIgnoreCase) ||
            (isDrift && ++lifecycleRebindProbeReadCount > 1);
        var workerOwned = destinationName != "B-ui-owned.ap21";
        var state = ProjectRebindStateInfo.Create(sourcePath, requestedDestination!, isModified, workerOwned);
        if (sourcePath.EndsWith("-missing-source.ap21", StringComparison.Ordinal))
        {
            state.SourceProjectPath = null;
        }
        else if (sourcePath.EndsWith("-null-modified.ap21", StringComparison.Ordinal))
        {
            state.SourceIsModified = null;
        }
        else if (sourcePath.EndsWith("-wrong-destination.ap21", StringComparison.Ordinal))
        {
            state.DestinationProjectPath = @"C:\Lifecycle\Other.ap21";
        }
        else if (sourcePath.EndsWith("-wrong-disposition.ap21", StringComparison.Ordinal))
        {
            state.WillCloseSource = false;
        }

        return Success(WorkerJson.SerializePayload(state));
    }

    ProjectStatusInfo StatusWithMetadataFixture() => new()
    {
        IsOpen = true,
        Name = "Ground",
        Path = @"C:\Projects\Ground\Ground.ap21",
        Version = "V21",
        Author = "TiaBot",
        IsModified = false,
        CreationTime = new DateTime(2026, 1, 10, 8, 30, 0),
        LastModified = new DateTime(2026, 2, 14, 17, 5, 0),
        LastModifiedBy = "TiaBot",
        Size = 2048,
        Metadata = new ProjectMetadataInfo
        {
            Copyright = "© ACME Controls",
            Family = "Lines",
            Comment = new ProjectCommentInfo
            {
                Translations = new List<ProjectCommentTranslationInfo>
                {
                    new() { Culture = "en-US", Text = "Ground line" },
                    new() { Culture = "pt-BR", Text = "Linha de piso" },
                },
            },
            LanguageSettings = new ProjectLanguageSettingsInfo
            {
                Languages = new List<string> { "en-US", "de-DE", "pt-BR" },
                ActiveLanguages = new List<string> { "en-US", "pt-BR" },
                EditingLanguage = "en-US",
                ReferenceLanguage = "de-DE",
            },
            HistoryEntries = new List<ProjectHistoryEntryInfo>
            {
                new() { Text = "Project created", DateTime = new DateTime(2026, 1, 10, 8, 0, 0) },
                new() { Text = "Line imported", DateTime = new DateTime(2026, 1, 12, 9, 30, 0) },
            },
            HistoryTruncated = false,
            UsedProducts = new List<ProjectUsedProductInfo>
            {
                new() { Name = "S7-1500", Version = "V4.5" },
                new() { Name = "WinCC", Version = "V7.4" },
            },
            CompilationSettings = new ProjectCompilationSettingsInfo
            {
                IsSimulationDuringBlockCompilationEnabled = true,
                IsVirtualPlcDuringBlockCompilationEnabled = false,
            },
        },
    };

    public static string DirectStatusPayload(ProjectStatusInfo status) => WorkerJson.SerializePayload(new ProjectStatusResultInfo
    {
        Operation = "get_project_status", ProjectPath = status.Path, Project = status
    });

}
