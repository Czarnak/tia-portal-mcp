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

    public static WriteSafetyService Shared { get; } = new();

    private readonly ConcurrentDictionary<string, SafetyTokenEntry> _tokens = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _getUtcNow;
    private readonly TimeSpan _tokenLifetime;

    public WriteSafetyService()
        : this(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10))
    {
    }

    public WriteSafetyService(Func<DateTimeOffset> getUtcNow)
        : this(getUtcNow, TimeSpan.FromMinutes(10))
    {
    }

    public WriteSafetyService(Func<DateTimeOffset> getUtcNow, TimeSpan tokenLifetime)
    {
        _getUtcNow = getUtcNow;
        _tokenLifetime = tokenLifetime;
    }

    public string CreatePreview(
        string toolName,
        string? projectPath,
        object target,
        string summary,
        object requestedInput,
        string currentState,
        string? diff = null)
    {
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
                diff
            },
            JsonOptions);
    }

    public WriteSafetyValidationResult ValidateAndConsume(
        string? safetyToken,
        string toolName,
        string? projectPath,
        object target,
        object requestedInput,
        string currentState)
    {
        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            return WriteSafetyValidationResult.Invalid("Safety token required.");
        }

        if (!_tokens.TryRemove(safetyToken, out var entry))
        {
            return WriteSafetyValidationResult.Invalid("Safety token expired, consumed, or unknown.");
        }

        if (_getUtcNow() > entry.ExpiresAtUtc)
        {
            return WriteSafetyValidationResult.Invalid("Safety token expired.");
        }

        if (!string.Equals(entry.ToolName, toolName, StringComparison.Ordinal))
        {
            return WriteSafetyValidationResult.Invalid("Safety token was issued for a different tool.");
        }

        if (!string.Equals(entry.ProjectPath, NormalizeProjectPath(projectPath), StringComparison.OrdinalIgnoreCase))
        {
            return WriteSafetyValidationResult.Invalid("Safety token was issued for a different project path.");
        }

        if (!string.Equals(entry.TargetJson, ToStableJson(target), StringComparison.Ordinal))
        {
            return WriteSafetyValidationResult.Invalid("Safety token was issued for a different target.");
        }

        var requestedInputHash = HashText(ToStableJson(requestedInput));
        if (!string.Equals(entry.RequestedInputHash, requestedInputHash, StringComparison.Ordinal))
        {
            return WriteSafetyValidationResult.Invalid("Safety token input does not match this write request.");
        }

        var currentStateHash = HashText(currentState);
        if (!string.Equals(entry.CurrentStateHash, currentStateHash, StringComparison.Ordinal))
        {
            return WriteSafetyValidationResult.Invalid("Safety token current state no longer matches the project.");
        }

        return WriteSafetyValidationResult.Valid(requestedInputHash, currentStateHash);
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
            var directory = Path.Combine(
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
        catch
        {
            // Audit failures must not hide the write result from the MCP caller.
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
