using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Tools;

/// <summary>Renders one typed standalone outcome, bounding complete canonical values and warnings.</summary>
internal static class StructuredStandaloneResult
{
    internal const string Version = "1.0";

    internal static CallToolResult Create<TPayload>(
        string tool,
        StandaloneToolOutcome<TPayload>? outcome,
        IReadOnlyList<string> warnings,
        StructuredOperationFailure? rejection = null,
        string? omissionGuidance = null) where TPayload : class
    {
        if ((rejection is null) == (outcome is null))
            throw new ArgumentException("Provide either an outcome or a rejection.");

        var retainedWarnings = warnings.ToList();
        var valueChars = outcome?.Value is null ? 0 : CanonicalJson.Serialize(outcome.Value).Length;
        var guidance = omissionGuidance ?? (tool switch
        {
            "compile_check" => "Narrow the compile with plcName or blockPath. The complete report was omitted.",
            "read_cross_references" => "Narrow with maxResults, a narrower target path, or a member. The complete report was omitted.",
            "bind_project" => "Retry bind_project with the absolute projectPath of one open project to narrow the candidates.",
            _ => "Extended metadata was too large to return. This tool has no metadata selector; retry after reducing project metadata or inspect it in TIA Portal."
        });

        void Omit(string reason, int limit)
        {
            outcome = outcome! with
            {
                Status = OperationBatchStatus.Omitted,
                Value = null,
                Omission = new(reason, limit, valueChars, tool, guidance)
            };
        }

        object Compose()
        {
            var success = rejection is null && outcome?.Status == OperationBatchStatus.Succeeded;
            return tool switch
            {
                "get_project_status" when typeof(TPayload) == typeof(ProjectStatusInfo) =>
                    new GetProjectStatusResponse(Version, success, rejection, retainedWarnings,
                        (StandaloneToolOutcome<ProjectStatusInfo>?)(object?)outcome),
                "compile_check" when typeof(TPayload) == typeof(CompileCheckReport) =>
                    new CompileCheckResponse(Version, success, rejection, retainedWarnings,
                        (StandaloneToolOutcome<CompileCheckReport>?)(object?)outcome),
                "read_cross_references" when typeof(TPayload) == typeof(CrossReferenceReport) =>
                    new ReadCrossReferencesResponse(Version, success, rejection, retainedWarnings,
                        (StandaloneToolOutcome<CrossReferenceReport>?)(object?)outcome),
                "bind_project" when typeof(TPayload) == typeof(ProjectBindingResult) =>
                    new BindProjectResponse(Version, success, rejection, retainedWarnings,
                        (StandaloneToolOutcome<ProjectBindingResult>?)(object?)outcome),
                _ => throw new ArgumentException("Unknown standalone tool/payload pair.")
            };
        }

        if (valueChars > StructuredOperationBatchPayloadBudget.MaxItemChars)
            Omit(StructuredOperationBatchPayloadBudget.ItemLimitReason, StructuredOperationBatchPayloadBudget.MaxItemChars);

        var text = CanonicalJson.Serialize(Compose());
        if (text.Length > StructuredOperationBatchPayloadBudget.MaxDocumentChars && outcome?.Value is not null)
        {
            Omit(StructuredOperationBatchPayloadBudget.DocumentLimitReason, StructuredOperationBatchPayloadBudget.MaxDocumentChars);
            text = CanonicalJson.Serialize(Compose());
        }

        var omittedWarnings = 0;
        while (text.Length > StructuredOperationBatchPayloadBudget.MaxDocumentChars && retainedWarnings.Count > 0)
        {
            retainedWarnings.RemoveAt(retainedWarnings.Count - 1);
            omittedWarnings++;
            text = CanonicalJson.Serialize(Compose());
        }
        if (omittedWarnings > 0)
        {
            retainedWarnings.Add($"{omittedWarnings} warning entries were omitted to fit the response budget.");
            while (CanonicalJson.Serialize(Compose()).Length > StructuredOperationBatchPayloadBudget.MaxDocumentChars
                && retainedWarnings.Count > 1)
            {
                retainedWarnings.RemoveAt(retainedWarnings.Count - 2);
                omittedWarnings++;
                retainedWarnings[^1] = $"{omittedWarnings} warning entries were omitted to fit the response budget.";
            }
            text = CanonicalJson.Serialize(Compose());
        }

        if (text.Length > StructuredOperationBatchPayloadBudget.MaxDocumentChars)
        {
            // Last resort: preserve failure category and status, and disclose shortened prose.
            static StructuredOperationFailure? Shorten(StructuredOperationFailure? failure)
                => failure is null ? null : failure with { Message = failure.Message[..Math.Min(120, failure.Message.Length)] + " [message shortened]" };
            rejection = Shorten(rejection);
            if (outcome is not null) outcome = outcome with { Failure = Shorten(outcome.Failure) };
            text = CanonicalJson.Serialize(Compose());
        }

        return StructuredToolResult.CreateCanonical(text, isError: rejection is not null);
    }
}
