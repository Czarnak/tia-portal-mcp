using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TiaMcpServer.Safety;

public sealed class WriteSafetyService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromMinutes(10);

    public static WriteSafetyService Shared { get; } = new();

    private readonly ConcurrentDictionary<string, SafetyTokenEntry> _tokens = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _getUtcNow;
    private readonly TimeSpan _tokenLifetime;

    private readonly string? _auditDirectoryOverride;

    public WriteSafetyService()
        : this(() => DateTimeOffset.UtcNow, DefaultTokenLifetime)
    {
    }

    public WriteSafetyService(Func<DateTimeOffset> getUtcNow)
        : this(getUtcNow, DefaultTokenLifetime)
    {
    }

    public WriteSafetyService(Func<DateTimeOffset> getUtcNow, TimeSpan tokenLifetime, string? auditDirectory = null)
    {
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
       string? diff = null,
       string? instructions = null)
    {
        EvictExpiredTokens();

        var token = CreateToken();
        var targetJson = ToStableJson(target);
        var requestedInputJson = ToStableJson(requestedInput);
        var currentStateHash = HashText(currentState);
        var requestedInputHash = HashText(requestedInputJson);
        var expiresAtUtc = _getUtcNow().Add(_tokenLifetime);

        _tokens[token] = new SafetyTokenEntry(
            ToolName: toolName,
            ProjectPath: NormalizeProjectPath(projectPath),
            TargetJson: targetJson,
            RequestedInputHash: requestedInputHash,
            CurrentStateHash: currentStateHash,
            ExpiresAtUtc: expiresAtUtc);

        return JsonSerializer.Serialize(
            new
            {
                toolName,
                target,
                summary,
                currentStateHash,
                requestedInputHash,
                expiresAtUtc,
                safetyToken = token,
                diff,
                instructions
            },
            JsonOptions);
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
    {
        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            return Rejected("Safety token required.", previewToolName);
        }

        if (!_tokens.TryGetValue(safetyToken, out var entry))
        {
            return Rejected("Safety token expired, consumed, or unknown.", previewToolName);
        }

        if (_getUtcNow() > entry.ExpiresAtUtc)
        {
            return Rejected("Safety token expired.", previewToolName);
        }

        if (!string.Equals(entry.ToolName, toolName, StringComparison.Ordinal))
        {
            return Rejected("Safety token was issued for a different tool.", previewToolName);
        }

        if (!string.Equals(entry.ProjectPath, NormalizeProjectPath(projectPath), StringComparison.OrdinalIgnoreCase))
        {
            return Rejected("Safety token was issued for a different project path.", previewToolName);
        }

        if (!string.Equals(entry.TargetJson, ToStableJson(target), StringComparison.Ordinal))
        {
            return Rejected("Safety token was issued for a different target.", previewToolName);
        }

        var requestedInputHash = HashText(ToStableJson(requestedInput));
        if (!string.Equals(entry.RequestedInputHash, requestedInputHash, StringComparison.Ordinal))
        {
            return Rejected("Safety token input does not match this write request.", previewToolName);
        }

        return WriteSafetyValidationResult.Valid(requestedInputHash, entry.CurrentStateHash);
    }

    public WriteSafetyValidationResult ValidateAndConsume(
        string? safetyToken,
        string toolName,
        string? projectPath,
        object target,
        object requestedInput,
        string currentState,
        string? previewToolName = null)
    {
        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            return Rejected("Safety token required.", previewToolName);
        }

        if (!_tokens.TryRemove(safetyToken, out var entry))
        {
            return Rejected("Safety token expired, consumed, or unknown.", previewToolName);
        }

        if (_getUtcNow() > entry.ExpiresAtUtc)
        {
            return Rejected("Safety token expired.", previewToolName);
        }

        if (!string.Equals(entry.ToolName, toolName, StringComparison.Ordinal))
        {
            return Rejected("Safety token was issued for a different tool.", previewToolName);
        }

        if (!string.Equals(entry.ProjectPath, NormalizeProjectPath(projectPath), StringComparison.OrdinalIgnoreCase))
        {
            return Rejected("Safety token was issued for a different project path.", previewToolName);
        }

        if (!string.Equals(entry.TargetJson, ToStableJson(target), StringComparison.Ordinal))
        {
            return Rejected("Safety token was issued for a different target.", previewToolName);
        }

        var requestedInputHash = HashText(ToStableJson(requestedInput));
        if (!string.Equals(entry.RequestedInputHash, requestedInputHash, StringComparison.Ordinal))
        {
            return Rejected("Safety token input does not match this write request.", previewToolName);
        }

        var currentStateHash = HashText(currentState);
        if (!string.Equals(entry.CurrentStateHash, currentStateHash, StringComparison.Ordinal))
        {
            return Rejected("Safety token current state no longer matches the project.", previewToolName);
        }

        return WriteSafetyValidationResult.Valid(requestedInputHash, currentStateHash);
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

    private WriteSafetyValidationResult Rejected(string reason, string? previewToolName)
    {
        var previewTool = string.IsNullOrWhiteSpace(previewToolName) ? "the matching preview tool" : previewToolName;
        return WriteSafetyValidationResult.Invalid(
            $"{reason} Safety tokens are single-use and expire after {_tokenLifetime.TotalMinutes:N0} minutes. "
            + $"Call {previewTool} again to get a fresh token, review the new preview, then retry with confirm=true and the new safetyToken.");
    }

    public void AppendAudit(
        string toolName,
        string? projectPath,
        object target,
        object requestedInput,
        string currentState,
        string result)
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
            var record = JsonSerializer.Serialize(
                new
                {
                    timestampUtc = timestamp,
                    toolName,
                    projectPath = NormalizeProjectPath(projectPath),
                    target,
                    requestedInputHash = HashText(ToStableJson(requestedInput)),
                    currentStateHash = HashText(currentState),
                    resultHash = HashText(result),
                    resultPreview = result.Length <= 2000 ? result : result[..2000]
                },
                JsonOptions);

            File.AppendAllText(auditPath, record + Environment.NewLine, Encoding.UTF8);
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

    public static string HashText(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ToStableJson(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record SafetyTokenEntry(
        string ToolName,
        string ProjectPath,
        string TargetJson,
        string RequestedInputHash,
        string CurrentStateHash,
        DateTimeOffset ExpiresAtUtc);
}

public sealed record WriteSafetyValidationResult(
    bool IsValid,
    string Error,
    string? RequestedInputHash,
    string? CurrentStateHash)
{
    public static WriteSafetyValidationResult Valid(string requestedInputHash, string currentStateHash)
    {
        return new(true, string.Empty, requestedInputHash, currentStateHash);
    }

    public static WriteSafetyValidationResult Invalid(string error)
    {
        return new(false, error, null, null);
    }
}
