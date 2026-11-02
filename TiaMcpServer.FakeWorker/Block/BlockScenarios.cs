using System.Text.Json;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Diagnostics;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using static TiaMcpServer.FakeWorker.ScenarioSupport;

namespace TiaMcpServer.FakeWorker.Block;

internal sealed class BlockScenarios
{
    private int updateBlockPostconditionAttempt = 0;
    private int blockOutcomeUpdateAttempt = 0;
    private int createBlockPostconditionAttempt = 0;

    public void Handle(string line, string scenario, WorkerSessionIdentity? currentExpectedSessionIdentity,
        Action<string, bool> respond)
    {
        void Respond(string json, bool includeSessionIdentity = true) => respond(json, includeSessionIdentity);
        switch (scenario)
        {
            case "update-block-postcondition-failed":
                // Fixture bootstrap must not consume the protected write's attempt sequence.
                if (ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null)
                {
                    Respond("""{"success":true,"payload":"{\"isOpen\":true}"}""");
                }
                else
                {
                    updateBlockPostconditionAttempt++;
                    Respond(BlockOutcomeResponse(
                        success: false,
                        category: WorkerFailureCategories.PostconditionFailed,
                        message: $"block update verification failed on attempt {updateBlockPostconditionAttempt}",
                        outcome: CompletedBlockOutcome(SourceFormatNames.Xml, compileStage: "unavailable")));
                }
                break;
            case "block-outcome-success":
            case "block-outcome-postcondition":
            case "block-outcome-partial":
            case "block-outcome-missing-success":
            case "block-outcome-missing-failure":
            case "block-outcome-invalid":
            case "block-outcome-oversized":
                if (ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null)
                {
                    Respond("""{"success":true,"payload":"{\"isOpen\":true}"}""");
                }
                else if (ReadMethod(line) == "get_block_content")
                {
                    Respond("""{"success":true,"payload":"DATA_BLOCK \"Before\"\r\nBEGIN\r\nEND_DATA_BLOCK\r\n"}""");
                }
                else if (ReadMethod(line) == "update_block_logic")
                {
                    blockOutcomeUpdateAttempt++;
                    Respond(scenario switch
                    {
                        "block-outcome-success" => BlockOutcomeResponse(
                            true, null, $"update attempt {blockOutcomeUpdateAttempt}",
                            CompletedBlockOutcome(SourceFormatNames.Source, "succeeded")),
                        "block-outcome-postcondition" => BlockOutcomeResponse(
                            false, WorkerFailureCategories.PostconditionFailed,
                            $"postcondition attempt {blockOutcomeUpdateAttempt}",
                            CompletedBlockOutcome(SourceFormatNames.Source, "failed")),
                        "block-outcome-partial" => BlockOutcomeResponse(
                            true, null, $"partial attempt {blockOutcomeUpdateAttempt}",
                            CompletedBlockOutcome(SourceFormatNames.Source, "unavailable", partialReport: true)),
                        "block-outcome-missing-success" =>
                            $$"""{"success":true,"payload":"untrusted marker attempt {{blockOutcomeUpdateAttempt}}"}""",
                        "block-outcome-missing-failure" =>
                            $$"""{"success":false,"failureCategory":"postcondition_failed","error":"untrusted marker attempt {{blockOutcomeUpdateAttempt}}"}""",
                        "block-outcome-invalid" => BlockOutcomeResponse(
                            false, WorkerFailureCategories.PostconditionFailed,
                            $"untrusted marker attempt {blockOutcomeUpdateAttempt}",
                            CompletedBlockOutcome(SourceFormatNames.Source, "failed") with
                            {
                                TargetMutationCommitted = false
                            }),
                        "block-outcome-oversized" => BlockOutcomeResponse(
                            false, WorkerFailureCategories.PostconditionFailed,
                            $"untrusted marker attempt {blockOutcomeUpdateAttempt}",
                            OversizedBlockOutcome()),
                        _ => BlockOutcomeResponse(
                            false, WorkerFailureCategories.PostconditionFailed,
                            $"audit failure attempt {blockOutcomeUpdateAttempt}",
                            AuditFailureBlockOutcome())
                    });
                }
                else
                {
                    Respond($$"""{"success":false,"failureCategory":"validation_error","error":"unexpected method '{{ReadMethod(line)}}'"}""");
                }
                break;
            case "block-outcome-preimport-validation":
            case "block-outcome-preimport-access":
            case "block-outcome-preimport-no-project":
            case "block-outcome-preimport-project-open":
            case "block-outcome-preimport-identity":
                if (ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null)
                {
                    Respond("""{"success":true,"payload":"{\"isOpen\":true}"}""");
                }
                else
                {
                    var category = scenario switch
                    {
                        "block-outcome-preimport-validation" => WorkerFailureCategories.ValidationError,
                        "block-outcome-preimport-access" => WorkerFailureCategories.AccessDenied,
                        "block-outcome-preimport-identity" => WorkerFailureCategories.BindingConflict,
                        _ => WorkerFailureCategories.WorkerOperationFailed
                    };
                    Respond(BlockOutcomeResponse(
                        false,
                        category,
                        $"pre-import {scenario}",
                        NotStartedBlockOutcome(SourceFormatNames.Source)));
                }
                break;
            case "create-block-postcondition-failed":
                // Fixture bootstrap must not consume the protected write's attempt sequence.
                if (ReadMethod(line) == "get_project_status" && currentExpectedSessionIdentity is null)
                {
                    Respond("""{"success":true,"payload":"{\"isOpen\":true}"}""");
                }
                else
                {
                    createBlockPostconditionAttempt++;
                    Respond($$"""{"success":false,"failureCategory":"postcondition_failed","error":"block creation verification failed on attempt {{createBlockPostconditionAttempt}}","warnings":["Project state may have changed; inspect the project before retrying."]}""");
                }
                break;
        }
    }

    public static string BlockOutcomeResponse(
        bool success,
        string? category,
        string message,
        BlockImportOutcomeInfo outcome)
        => JsonSerializer.Serialize(new WorkerResponse
        {
            Success = success,
            Payload = success ? message : null,
            FailureCategory = category,
            Error = success ? null : message,
            Warnings = success
                ? null
                : new List<string>
                {
                    "Project state may have changed; inspect the project before retrying."
                },
            BlockImportOutcome = outcome
        }, WorkerJson.Envelope);

    public static BlockImportOutcomeInfo CompletedBlockOutcome(
        string format,
        string compileStage,
        bool partialReport = false)
    {
        CompileCheckReport? report = null;
        if (compileStage is "succeeded" or "failed" || partialReport)
        {
            var failed = compileStage == "failed";
            report = new CompileCheckReport
            {
                Scope = "block",
                BlockPath = "PLC_1/Blocks/Main",
                TotalErrorCount = failed ? 1 : 0,
                TotalWarningCount = partialReport ? 1 : 0,
                OverallState = failed ? "Error" : partialReport ? "Warning" : "Success",
                Plcs = new List<PlcCompileInfo>
                {
                    new()
                    {
                        PlcName = "PLC_1",
                        DeviceName = "Device_1",
                        State = failed ? "Error" : partialReport ? "Warning" : "Success",
                        ErrorCount = failed ? 1 : 0,
                        WarningCount = partialReport ? 1 : 0,
                        Messages = failed
                            ? new List<CompileMessageInfo>
                            {
                                new() { Description = "Compile failed.", Path = "PLC_1/Blocks/Main", Severity = "Error" }
                            }
                            : new List<CompileMessageInfo>(),
                        DiagnosticNotes = new List<string>()
                    }
                }
            };
        }

        return new BlockImportOutcomeInfo
        {
            ImportStage = "completed",
            ImportResultState = compileStage == "failed" ? "non_success" : "success",
            TargetMutationCommitted = true,
            CompileStage = compileStage,
            CompileReport = report,
            CompileDetailsOmitted = partialReport,
            FinalReadStage = "succeeded",
            TargetPresent = true,
            ContentRelation = "unknown",
            TemporarySourceState = format == SourceFormatNames.Xml ? "not_applicable" : "removed"
        };
    }

    BlockImportOutcomeInfo NotStartedBlockOutcome(string format)
        => new()
        {
            ImportStage = "not_started",
            ImportResultState = "unavailable",
            TargetMutationCommitted = false,
            CompileStage = "not_started",
            FinalReadStage = "not_started",
            ContentRelation = "unknown",
            TemporarySourceState = format == SourceFormatNames.Xml ? "not_applicable" : "not_created"
        };

    BlockImportOutcomeInfo AuditFailureBlockOutcome()
    {
        var outcome = CompletedBlockOutcome(SourceFormatNames.Source, "unavailable", partialReport: true);
        var messages = Enumerable.Range(1, 20)
            .Select(index => new CompileMessageInfo
            {
                Description = $"Sanitized diagnostic {index:D2}",
                Path = $"PLC_1/Blocks/Item{index:D2}",
                Severity = "Warning"
            })
            .ToList();
        outcome.CompileReport!.Plcs[0].Messages = messages;
        outcome.CompileReport.Plcs[0].WarningCount = messages.Count;
        outcome.CompileReport.TotalWarningCount = messages.Count;
        return outcome with
        {
            ImportResultState = "non_success",
            CompileDetailsOmitted = true
        };
    }

    BlockImportOutcomeInfo OversizedBlockOutcome()
    {
        var escaped = new string('\\', 256);
        return CompletedBlockOutcome(SourceFormatNames.Source, "unavailable", partialReport: true) with
        {
            CompileReport = new CompileCheckReport
            {
                Scope = "block",
                OverallState = "Warning",
                TotalWarningCount = 1,
                Plcs = Enumerable.Range(0, 8).Select(_ => new PlcCompileInfo
                {
                    PlcName = escaped,
                    DeviceName = escaped,
                    State = escaped,
                    Messages = new List<CompileMessageInfo>(),
                    DiagnosticNotes = new List<string>()
                }).ToList()
            }
        };
    }
}
