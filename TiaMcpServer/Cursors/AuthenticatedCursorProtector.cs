using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;

namespace TiaMcpServer.Cursors;

internal enum AuthenticatedCursorStatus
{
    Success,
    ForeignProcess,
}

internal sealed record AuthenticatedCursorResult<TState>(
    AuthenticatedCursorStatus Status,
    TState? State)
    where TState : class;

internal sealed class AuthenticatedCursorException : Exception
{
    internal AuthenticatedCursorException()
        : base("The supplied authenticated cursor is invalid.")
    {
    }
}

internal sealed class AuthenticatedCursorSizeException(int cursorChars, int limitChars)
    : InvalidOperationException("The authenticated cursor exceeds its character limit.")
{
    internal int CursorChars { get; } = cursorChars;
    internal int LimitChars { get; } = limitChars;
}

internal sealed class AuthenticatedCursorProtector : IDisposable
{
    private const int CurrentFormatVersion = 1;
    private const int KeySizeBytes = 32;
    private const int SignatureSizeBytes = 32;
    private const int MaxCursorChars = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex Base64Url = new("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant);
    private static readonly string[] EnvelopeMembers =
    {
        "formatVersion",
        "processInstanceId",
        "purpose",
        "state",
    };

    private readonly byte[] _key;
    private readonly string _processInstanceId;
    private bool _disposed;

    internal AuthenticatedCursorProtector(byte[] key, string processInstanceId)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException("Authenticated cursor keys must contain exactly 32 bytes.", nameof(key));
        }

        ValidateProcessInstanceId(processInstanceId, nameof(processInstanceId));
        _key = (byte[])key.Clone();
        _processInstanceId = processInstanceId;
    }

    internal static AuthenticatedCursorProtector CreateProcessScoped()
    {
        var key = new byte[KeySizeBytes];
        RandomNumberGenerator.Fill(key);
        try
        {
            return new AuthenticatedCursorProtector(key, Guid.NewGuid().ToString("N"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal string Protect<TState>(string purpose, TState state)
        where TState : class
    {
        ThrowIfDisposed();
        ValidatePurpose(purpose);
        ArgumentNullException.ThrowIfNull(state);

        var payload = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(
            new Envelope<TState>(CurrentFormatVersion, _processInstanceId, purpose, state)));
        var signature = ComputeSignature(payload);
        var cursor = $"{EncodeBase64Url(payload)}.{EncodeBase64Url(signature)}";
        if (cursor.Length > MaxCursorChars)
        {
            throw new AuthenticatedCursorSizeException(cursor.Length, MaxCursorChars);
        }

        return cursor;
    }

    internal AuthenticatedCursorResult<TState> Unprotect<TState>(string purpose, string cursor)
        where TState : class
    {
        ThrowIfDisposed();
        ValidatePurpose(purpose);

        try
        {
            if (cursor is null || cursor.Length > MaxCursorChars)
            {
                throw new FormatException();
            }

            var parts = cursor.Split('.');
            if (parts.Length != 2)
            {
                throw new FormatException();
            }

            var payload = DecodeBase64Url(parts[0]);
            var suppliedSignature = DecodeBase64Url(parts[1]);
            if (suppliedSignature.Length != SignatureSizeBytes)
            {
                throw new CryptographicException();
            }

            var json = StrictUtf8.GetString(payload);
            var untypedEnvelope = ReadAndValidateUntypedEnvelope(json, payload);
            if (!string.Equals(
                    untypedEnvelope.ProcessInstanceId,
                    _processInstanceId,
                    StringComparison.Ordinal))
            {
                return new AuthenticatedCursorResult<TState>(
                    AuthenticatedCursorStatus.ForeignProcess,
                    State: null);
            }

            var expectedSignature = ComputeSignature(payload);
            if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            {
                throw new CryptographicException();
            }

            if (!string.Equals(untypedEnvelope.Purpose, purpose, StringComparison.Ordinal))
            {
                throw new FormatException();
            }

            var envelope = CanonicalJson.Deserialize<Envelope<TState>>(json);
            if (envelope.State is null
                || envelope.FormatVersion != CurrentFormatVersion
                || !string.Equals(envelope.ProcessInstanceId, _processInstanceId, StringComparison.Ordinal)
                || !string.Equals(envelope.Purpose, purpose, StringComparison.Ordinal))
            {
                throw new JsonException("Authenticated cursor envelope values are invalid.");
            }

            var canonicalPayload = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(envelope));
            if (!payload.AsSpan().SequenceEqual(canonicalPayload))
            {
                throw new JsonException("Authenticated cursor payload must use canonical JSON.");
            }

            return new AuthenticatedCursorResult<TState>(AuthenticatedCursorStatus.Success, envelope.State);
        }
        catch (Exception exception) when (exception is ArgumentException
            or CryptographicException
            or DecoderFallbackException
            or FormatException
            or InvalidOperationException
            or JsonException)
        {
            throw new AuthenticatedCursorException();
        }
    }

    /// <exception cref="CursorException">Null, invalid or oversized state: invalid_cursor; size data is the inner exception.</exception>
    internal string Seal<TState>(CursorKind kind, TState state, Func<TState, bool> isValid)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(isValid);
        if (state is null || !isValid(state))
        {
            throw new CursorException(WorkerFailureCategories.InvalidCursor, kind.InvalidMessage);
        }

        try
        {
            return Protect(kind.Purpose, state);
        }
        catch (AuthenticatedCursorSizeException size)
        {
            throw new CursorException(WorkerFailureCategories.InvalidCursor, kind.InvalidMessage, size);
        }
    }

    /// <exception cref="CursorException">Other process: the kind's foreign-process category; anything else: invalid_cursor.</exception>
    internal TState Open<TState>(CursorKind kind, string cursor, Func<TState, bool> isValid)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(isValid);
        AuthenticatedCursorResult<TState> result;
        try
        {
            result = Unprotect<TState>(kind.Purpose, cursor);
        }
        catch (Exception exception) when (exception is AuthenticatedCursorException or JsonException)
        {
            throw new CursorException(WorkerFailureCategories.InvalidCursor, kind.InvalidMessage);
        }

        if (result.Status == AuthenticatedCursorStatus.ForeignProcess)
        {
            throw new CursorException(kind.ForeignProcessCategory, kind.InvalidMessage);
        }

        if (result.State is null || !isValid(result.State))
        {
            throw new CursorException(WorkerFailureCategories.InvalidCursor, kind.InvalidMessage);
        }

        return result.State;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }

    private UntypedEnvelope ReadAndValidateUntypedEnvelope(string json, byte[] payload)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var root = document.RootElement;
        ValidateExactObject(root, EnvelopeMembers);

        var formatVersion = root.GetProperty("formatVersion").GetInt32();
        var processInstanceId = root.GetProperty("processInstanceId").GetString();
        var purpose = root.GetProperty("purpose").GetString();
        if (formatVersion != CurrentFormatVersion
            || string.IsNullOrWhiteSpace(processInstanceId)
            || string.IsNullOrWhiteSpace(purpose))
        {
            throw new JsonException("Authenticated cursor envelope values are invalid.");
        }

        var state = root.GetProperty("state").Clone();
        var canonicalPayload = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(
            new Envelope<JsonElement>(formatVersion, processInstanceId, purpose, state)));
        if (!payload.AsSpan().SequenceEqual(canonicalPayload))
        {
            throw new JsonException("Authenticated cursor payload must use canonical JSON.");
        }

        return new UntypedEnvelope(processInstanceId, purpose);
    }

    private byte[] ComputeSignature(byte[] payload)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(payload);
    }

    private static string EncodeBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        if (string.IsNullOrEmpty(value) || !Base64Url.IsMatch(value) || value.Length % 4 == 1)
        {
            throw new FormatException();
        }

        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        var decoded = Convert.FromBase64String(normalized);
        if (!string.Equals(value, EncodeBase64Url(decoded), StringComparison.Ordinal))
        {
            throw new FormatException();
        }

        return decoded;
    }

    private static void ValidateExactObject(JsonElement element, IReadOnlyCollection<string> requiredMembers)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Authenticated cursor envelope must be an object.");
        }

        var required = new HashSet<string>(requiredMembers, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!required.Contains(property.Name) || !seen.Add(property.Name))
            {
                throw new JsonException("Authenticated cursor envelope members are invalid.");
            }
        }

        if (seen.Count != required.Count)
        {
            throw new JsonException("Authenticated cursor envelope members are incomplete.");
        }
    }

    private static void ValidatePurpose(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw new ArgumentException("Authenticated cursor purposes must not be empty.", nameof(purpose));
        }
    }

    private static void ValidateProcessInstanceId(string processInstanceId, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(processInstanceId))
        {
            throw new ArgumentException("Process instance IDs must not be empty.", parameterName);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record Envelope<TState>(
        int FormatVersion,
        string ProcessInstanceId,
        string Purpose,
        TState State);

    private sealed record UntypedEnvelope(string ProcessInstanceId, string Purpose);
}
