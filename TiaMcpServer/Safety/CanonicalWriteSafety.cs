using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.Safety;

/// <summary>
/// Typed preview data returned by <see cref="WriteSafetyService.CreateCanonicalPreview"/>.
///
/// <para>
/// The target stays a live <typeparamref name="TTarget"/> rather than pre-rendered text, so an
/// opt-in tool can publish it inside its own declared output schema instead of embedding a JSON
/// string a consumer would have to parse a second time.
/// </para>
/// </summary>
public sealed record CanonicalWritePreview<TTarget>(
    TTarget Target,
    string Summary,
    string CurrentStateHash,
    string RequestedInputHash,
    DateTimeOffset ExpiresAtUtc,
    string SafetyToken,
    ProjectBindingSnapshot ProjectBinding,
    JsonElement? Diff,
    string Instructions);

/// <summary>
/// Opt-in canonical entry points. They are ADDITIONS: the presentation-serializer methods keep
/// their exact current behaviour for generic batches, type writes, and lifecycle tools.
///
/// <para>
/// Binding through <see cref="CanonicalJson"/> is what makes a token survive a difference that is
/// pure object-property order while still rejecting a changed value, a changed type, or a changed
/// array order. Every method below serializes each logical value exactly once and hands the
/// resulting text to the same private token and audit primitives the text path uses, so the two
/// presentations cannot drift apart on expiry, single use, or what is bound.
/// </para>
/// </summary>
public sealed partial class WriteSafetyService
{
    public CanonicalWritePreview<TTarget> CreateCanonicalPreview<TTarget, TInput, TState>(
        string toolName,
        string? projectPath,
        TTarget target,
        string summary,
        TInput requestedInput,
        TState currentState,
        string instructions,
        JsonElement? diff = null)
    {
        var issued = IssueToken(
            toolName,
            projectPath,
            CanonicalJson.Serialize(target),
            CanonicalJson.Serialize(requestedInput),
            CanonicalJson.Serialize(currentState));

        return new CanonicalWritePreview<TTarget>(
            target,
            summary,
            issued.CurrentStateHash,
            issued.RequestedInputHash,
            issued.ExpiresAtUtc,
            issued.Token,
            issued.ProjectBinding,
            diff,
            instructions);
    }

    /// <summary>
    /// Canonical counterpart of <see cref="ValidateEnvelope"/>: rejects a dead token before the
    /// expensive pre-apply state read, without consuming it. <see cref="ValidateAndConsumeCanonical"/>
    /// still re-checks everything atomically afterwards.
    /// </summary>
    public WriteSafetyValidationResult ValidateCanonicalEnvelope<TTarget, TInput>(
        string? safetyToken,
        string toolName,
        string? projectPath,
        TTarget target,
        TInput requestedInput,
        string? previewToolName = null)
        => ValidateEnvelopeCore(
            safetyToken,
            toolName,
            projectPath,
            CanonicalJson.Serialize(target),
            CanonicalJson.Serialize(requestedInput),
            previewToolName);

    public WriteSafetyValidationResult ValidateAndConsumeCanonical<TTarget, TInput, TState>(
        string? safetyToken,
        string toolName,
        string? projectPath,
        TTarget target,
        TInput requestedInput,
        TState currentState,
        string? previewToolName = null)
        => ValidateAndConsumeCore(
            safetyToken,
            toolName,
            projectPath,
            CanonicalJson.Serialize(target),
            CanonicalJson.Serialize(requestedInput),
            CanonicalJson.Serialize(currentState),
            previewToolName);

    /// <summary>
    /// Appends an audit record whose hashes use the same canonical representations the token was
    /// bound to, and which carries <paramref name="result"/> as the exact response document —
    /// stored as JSON, not as an escaped string an operator would have to unwrap.
    /// </summary>
    public void AppendCanonicalAudit<TTarget, TInput, TState, TResult>(
        string toolName,
        string? projectPath,
        TTarget target,
        TInput requestedInput,
        TState currentState,
        TResult result)
    {
        var targetText = CanonicalJson.Serialize(target);
        var resultText = CanonicalJson.Serialize(result);
        var requestedInputHash = HashText(CanonicalJson.Serialize(requestedInput));
        var currentStateHash = HashText(CanonicalJson.Serialize(currentState));
        var resultHash = HashText(resultText);

        AppendAuditRecord(toolName, timestamp => CanonicalJson.Serialize(
            new CanonicalAuditRecord(
                timestamp,
                toolName,
                NormalizeProjectPath(projectPath),
                _projectSessionBinding.CaptureSnapshot(),
                Detach(targetText),
                requestedInputHash,
                currentStateHash,
                resultHash,
                Detach(resultText))));
    }

    private static JsonElement Detach(string canonicalText)
    {
        using var document = JsonDocument.Parse(canonicalText);
        return document.RootElement.Clone();
    }

    private sealed record CanonicalAuditRecord(
        DateTimeOffset TimestampUtc,
        string ToolName,
        string ProjectPath,
        ProjectBindingSnapshot ProjectBinding,
        JsonElement Target,
        string RequestedInputHash,
        string CurrentStateHash,
        string ResultHash,
        JsonElement Result);
}
