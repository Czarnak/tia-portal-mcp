using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.Safety;

/// <summary>
/// Issues, validates, consumes, and audits write-safety tokens.
///
/// <para>
/// Two presentations share one set of private primitives. The methods on this file render and
/// bind through the PRESENTATION serializer and are what generic batches, type writes, and
/// lifecycle tools use. The opt-in canonical methods in <c>CanonicalWriteSafety.cs</c> bind
/// through <see cref="Json.CanonicalJson"/> instead. Only the rendering differs — expiry,
/// single use, tool/path/target/input/state binding, and audit behaviour are the same code.
/// </para>
/// </summary>
public sealed partial class WriteSafetyService
{
    public static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, SafetyTokenEntry> _tokens = new(StringComparer.Ordinal);
    private readonly ProjectSessionBinding _projectSessionBinding;
    private readonly Func<DateTimeOffset> _getUtcNow;
    private readonly TimeSpan _tokenLifetime;

    private readonly string? _auditDirectoryOverride;

    public WriteSafetyService()
        : this(new ProjectSessionBinding(null), () => DateTimeOffset.UtcNow, DefaultTokenLifetime)
    {
    }

    public WriteSafetyService(ProjectSessionBinding projectSessionBinding)
        : this(projectSessionBinding, () => DateTimeOffset.UtcNow, DefaultTokenLifetime)
    {
    }

    public WriteSafetyService(Func<DateTimeOffset> getUtcNow)
        : this(new ProjectSessionBinding(null), getUtcNow, DefaultTokenLifetime)
    {
    }

    public WriteSafetyService(Func<DateTimeOffset> getUtcNow, TimeSpan tokenLifetime, string? auditDirectory = null)
        : this(new ProjectSessionBinding(null), getUtcNow, tokenLifetime, auditDirectory)
    {
    }

    public WriteSafetyService(
        ProjectSessionBinding projectSessionBinding,
        Func<DateTimeOffset> getUtcNow,
        TimeSpan tokenLifetime,
        string? auditDirectory = null)
    {
        _projectSessionBinding = projectSessionBinding ?? throw new ArgumentNullException(nameof(projectSessionBinding));
        _getUtcNow = getUtcNow;
        _tokenLifetime = tokenLifetime;
        _auditDirectoryOverride = auditDirectory;
    }

    public string CreatePreview(
       string toolName,
       string? projectPath,
       object target,
       string summary,
       object requestedInput,
       string currentState,
       object? diff = null,
       string? instructions = null)
    {
        var issued = IssueToken(
            toolName,
            projectPath,
            ToStableJson(target),
            ToStableJson(requestedInput),
            currentState);

        return JsonSerializer.Serialize(
            new
            {
                toolName,
                target,
                summary,
                currentStateHash = issued.CurrentStateHash,
                requestedInputHash = issued.RequestedInputHash,
                expiresAtUtc = issued.ExpiresAtUtc,
                safetyToken = issued.Token,
                projectBinding = issued.ProjectBinding,
                diff,
                instructions
            },
            TiaJson.Presentation);
    }

    /// <summary>
    /// Cheap pre-check of everything a token binds EXCEPT current project state: existence,
    /// expiry, tool, project path, target, and requested input. Does not consume the token.
    /// Callers still must run <see cref="ValidateAndConsume"/> (which re-checks everything
    /// atomically) after reading current state; this exists so a dead token is rejected
    /// before the expensive pre-apply state read.
    /// </summary>
    public WriteSafetyValidationResult ValidateEnvelope(
        string? safetyToken,
        string toolName,
        string? projectPath,
        object target,
        object requestedInput,
        string? previewToolName = null)
        => ValidateEnvelopeCore(
            safetyToken,
            toolName,
            projectPath,
            ToStableJson(target),
            ToStableJson(requestedInput),
            previewToolName);

    /// <summary>
    /// Validates only token existence, expiry, and tool identity, then returns the exact binding to
    /// lease. Project path, target, input, and current-state validation remain mandatory inside the
    /// lease; deferring them preserves target-resolution semantics for state-derived selectors.
    /// </summary>
    public WriteSafetyValidationResult ValidateLeaseEnvelope(
        string? safetyToken,
        string toolName,
        string? previewToolName = null)
        => ValidateLeaseEnvelopeCore(
            safetyToken,
            toolName,
            previewToolName);

    public WriteSafetyValidationResult ValidateAndConsume(
        string? safetyToken,
        string toolName,
        string? projectPath,
        object target,
        object requestedInput,
        string currentState,
        string? previewToolName = null)
        => ValidateAndConsumeCore(
            safetyToken,
            toolName,
            projectPath,
            ToStableJson(target),
            ToStableJson(requestedInput),
            currentState,
            previewToolName);

    /// <summary>
    /// Stores one token bound to already-rendered target/input/state text. Both presentations
    /// route through here, so a token's binding rules cannot diverge between them.
    /// </summary>
    private IssuedToken IssueToken(
        string toolName,
        string? projectPath,
        string targetJson,
        string requestedInputJson,
        string currentStateJson)
    {
        EvictExpiredTokens();

        var token = CreateToken();
        var requestedInputHash = HashText(requestedInputJson);
        var currentStateHash = HashText(currentStateJson);
        var expiresAtUtc = _getUtcNow().Add(_tokenLifetime);
        var projectBinding = _projectSessionBinding.CaptureSnapshot();

        _tokens[token] = new SafetyTokenEntry(
            ToolName: toolName,
            ProjectPath: ResolveTokenProjectPath(projectPath, projectBinding),
            ProjectBinding: projectBinding,
            TargetJson: targetJson,
            RequestedInputHash: requestedInputHash,
            CurrentStateHash: currentStateHash,
            ExpiresAtUtc: expiresAtUtc);

        return new IssuedToken(token, requestedInputHash, currentStateHash, expiresAtUtc, projectBinding);
    }

    private WriteSafetyValidationResult ValidateEnvelopeCore(
        string? safetyToken,
        string toolName,
        string? projectPath,
        string targetJson,
        string requestedInputJson,
        string? previewToolName)
    {
        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            return Rejected("Safety token required.", previewToolName, WorkerFailureCategories.ValidationError);
        }

        if (!_tokens.TryGetValue(safetyToken, out var entry))
        {
            return Rejected("Safety token expired, consumed, or unknown.", previewToolName, WorkerFailureCategories.ValidationError);
        }

        return MatchEntry(
            entry, toolName, projectPath, targetJson, requestedInputJson, currentStateJson: null, previewToolName);
    }

    private WriteSafetyValidationResult ValidateLeaseEnvelopeCore(
        string? safetyToken,
        string toolName,
        string? previewToolName)
    {
        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            return Rejected("Safety token required.", previewToolName, WorkerFailureCategories.ValidationError);
        }

        if (!_tokens.TryGetValue(safetyToken, out var entry))
        {
            return Rejected("Safety token expired, consumed, or unknown.", previewToolName, WorkerFailureCategories.ValidationError);
        }

        if (_getUtcNow() > entry.ExpiresAtUtc)
        {
            return Rejected("Safety token expired.", previewToolName, WorkerFailureCategories.ValidationError);
        }

        if (!string.Equals(entry.ToolName, toolName, StringComparison.Ordinal))
        {
            return Rejected("Safety token was issued for a different tool.", previewToolName, WorkerFailureCategories.ValidationError);
        }

        return WriteSafetyValidationResult.Valid(
            entry.RequestedInputHash,
            entry.CurrentStateHash,
            entry.ProjectBinding);
    }

    private WriteSafetyValidationResult ValidateAndConsumeCore(
        string? safetyToken,
        string toolName,
        string? projectPath,
        string targetJson,
        string requestedInputJson,
        string currentStateJson,
        string? previewToolName)
    {
        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            return Rejected("Safety token required.", previewToolName, WorkerFailureCategories.ValidationError);
        }

        // Removed first: a token is spent by the attempt, not by the attempt succeeding.
        if (!_tokens.TryRemove(safetyToken, out var entry))
        {
            return Rejected("Safety token expired, consumed, or unknown.", previewToolName, WorkerFailureCategories.ValidationError);
        }

        return MatchEntry(
            entry, toolName, projectPath, targetJson, requestedInputJson, currentStateJson, previewToolName);
    }

    /// <summary>
    /// Checks everything a token binds. <paramref name="currentStateJson"/> is null for the
    /// envelope pre-check, which deliberately cannot see project state yet.
    /// </summary>
    private WriteSafetyValidationResult MatchEntry(
        SafetyTokenEntry entry,
        string toolName,
        string? projectPath,
        string targetJson,
        string requestedInputJson,
        string? currentStateJson,
        string? previewToolName)
    {
        if (_getUtcNow() > entry.ExpiresAtUtc)
        {
            return Rejected("Safety token expired.", previewToolName, WorkerFailureCategories.ValidationError);
        }

        if (!string.Equals(entry.ToolName, toolName, StringComparison.Ordinal))
        {
            return Rejected("Safety token was issued for a different tool.", previewToolName, WorkerFailureCategories.ValidationError);
        }

        var currentBinding = _projectSessionBinding.CaptureSnapshot();
        if (!entry.ProjectBinding.SameBinding(currentBinding))
        {
            return Rejected(
                "Safety token was issued for a different worker/Portal/project session binding.",
                previewToolName,
                WorkerFailureCategories.BindingConflict);
        }

        if (!string.Equals(
                entry.ProjectPath,
                ResolveTokenProjectPath(projectPath, currentBinding),
                StringComparison.OrdinalIgnoreCase))
        {
            return Rejected("Safety token was issued for a different project path.", previewToolName, WorkerFailureCategories.BindingConflict);
        }

        if (!string.Equals(entry.TargetJson, targetJson, StringComparison.Ordinal))
        {
            return Rejected("Safety token was issued for a different target.", previewToolName, WorkerFailureCategories.ValidationError);
        }

        var requestedInputHash = HashText(requestedInputJson);
        if (!string.Equals(entry.RequestedInputHash, requestedInputHash, StringComparison.Ordinal))
        {
            return Rejected("Safety token input does not match this write request.", previewToolName, WorkerFailureCategories.ValidationError);
        }

        if (currentStateJson is null)
        {
            return WriteSafetyValidationResult.Valid(
                requestedInputHash,
                entry.CurrentStateHash,
                entry.ProjectBinding);
        }

        var currentStateHash = HashText(currentStateJson);
        if (!string.Equals(entry.CurrentStateHash, currentStateHash, StringComparison.Ordinal))
        {
            return Rejected("Safety token current state no longer matches the project.", previewToolName, WorkerFailureCategories.StateChanged);
        }

        return WriteSafetyValidationResult.Valid(
            requestedInputHash,
            currentStateHash,
            entry.ProjectBinding);
    }

    /// <summary>Number of live (unconsumed, possibly expired) tokens. Test hook.</summary>
    internal int ActiveTokenCount => _tokens.Count;

    /// <summary>
    /// Drops expired tokens so an abandoned preview cannot grow memory forever.
    /// Swept on every CreatePreview — no timer needed; expiry is still re-checked on consume.
    /// </summary>
    private void EvictExpiredTokens()
    {
        var now = _getUtcNow();
        foreach (var pair in _tokens)
        {
            if (now > pair.Value.ExpiresAtUtc)
            {
                _tokens.TryRemove(pair.Key, out _);
            }
        }
    }

    private WriteSafetyValidationResult Rejected(string reason, string? previewToolName, string failureCategory)
    {
        var previewTool = string.IsNullOrWhiteSpace(previewToolName) ? "the matching preview tool" : previewToolName;
        return WriteSafetyValidationResult.Invalid(
            $"{reason} Safety tokens are single-use and expire after {_tokenLifetime.TotalMinutes:N0} minutes. "
            + $"Call {previewTool} again to get a fresh token, review the new preview, then retry with confirm=true and the new safetyToken.",
            failureCategory);
    }

    public void AppendAudit(
        string toolName,
        string? projectPath,
        object target,
        object requestedInput,
        string currentState,
        string result)
        => AppendAuditRecord(toolName, timestamp => JsonSerializer.Serialize(
            new
            {
                timestampUtc = timestamp,
                toolName,
                projectPath = NormalizeProjectPath(projectPath),
                projectBinding = _projectSessionBinding.CaptureSnapshot(),
                target,
                requestedInputHash = HashText(ToStableJson(requestedInput)),
                currentStateHash = HashText(currentState),
                resultHash = HashText(result),
                resultPreview = result.Length <= 2000 ? result : result[..2000]
            },
            TiaJson.Presentation));

    /// <summary>
    /// Appends one already-rendered JSONL record. <paramref name="render"/> receives the same
    /// timestamp the file name is derived from, so a record can never claim a different day than
    /// the file it lives in.
    /// </summary>
    private void AppendAuditRecord(string toolName, Func<DateTimeOffset, string> render)
    {
        try
        {
            var directory = _auditDirectoryOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TiaMcpServer",
                "audit");
            Directory.CreateDirectory(directory);

            var timestamp = _getUtcNow();
            var auditPath = Path.Combine(directory, $"{timestamp:yyyy-MM-dd}.jsonl");
            File.AppendAllText(auditPath, render(timestamp) + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            // Audit failures must not hide the write result from the MCP caller,
            // but a broken audit trail must be visible to the operator.
            Console.Error.WriteLine($"TiaMcpServer: failed to write audit record for '{toolName}': {ex.Message}");
        }
    }

    public static string NormalizeProjectPath(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return "(active)";
        }

        try
        {
            return Path.GetFullPath(projectPath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return projectPath.Trim();
        }
    }

    private static string ResolveTokenProjectPath(
        string? requestedProjectPath,
        ProjectBindingSnapshot binding)
    {
        if (binding.IsVerified && !string.IsNullOrWhiteSpace(binding.ProjectPath))
        {
            var requested = string.IsNullOrWhiteSpace(requestedProjectPath)
                ? binding.ProjectPath
                : NormalizeProjectPath(requestedProjectPath);
            return requested!;
        }

        return NormalizeProjectPath(requestedProjectPath);
    }

    public static string HashText(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ToStableJson(object value)
    {
        return JsonSerializer.Serialize(value, TiaJson.Presentation);
    }

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>A freshly issued token and the binding hashes a preview must report.</summary>
    private sealed record IssuedToken(
        string Token,
        string RequestedInputHash,
        string CurrentStateHash,
        DateTimeOffset ExpiresAtUtc,
        ProjectBindingSnapshot ProjectBinding);

    private sealed record SafetyTokenEntry(
        string ToolName,
        string ProjectPath,
        ProjectBindingSnapshot ProjectBinding,
        string TargetJson,
        string RequestedInputHash,
        string CurrentStateHash,
        DateTimeOffset ExpiresAtUtc);
}

public sealed record WriteSafetyValidationResult(
    bool IsValid,
    string Error,
    string? RequestedInputHash,
    string? CurrentStateHash,
    string? FailureCategory = null,
    ProjectBindingSnapshot? ProjectBinding = null)
{
    public static WriteSafetyValidationResult Valid(
        string requestedInputHash,
        string currentStateHash,
        ProjectBindingSnapshot projectBinding)
    {
        return new(
            true,
            string.Empty,
            requestedInputHash,
            currentStateHash,
            FailureCategory: null,
            ProjectBinding: projectBinding);
    }

    /// <summary>
    /// Builds an invalid result carrying an explicit <paramref name="failureCategory"/> from the
    /// closed <see cref="WorkerFailureCategories"/> vocabulary. The category is threaded from the
    /// specific rejection reason at the call site — never inferred by parsing <paramref name="error"/>.
    /// </summary>
    public static WriteSafetyValidationResult Invalid(string error, string failureCategory)
    {
        return new(false, error, null, null, failureCategory, ProjectBinding: null);
    }
}
