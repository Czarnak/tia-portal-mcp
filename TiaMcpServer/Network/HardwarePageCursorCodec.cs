using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.Network;

internal sealed class HardwarePageCursorException : Exception
{
    internal HardwarePageCursorException()
        : base("The supplied hardware page cursor is invalid.")
    {
    }

    internal string Category => WorkerFailureCategories.InvalidCursor;
}

internal sealed class HardwarePageCursorCodec
{
    private const int CurrentVersion = 1;
    private const int KeySizeBytes = 32;
    private const int SignatureSizeBytes = 32;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex Base64Url = new("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex LowercaseSha256 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly string[] PayloadMembers =
    {
        "version",
        "resolvedProjectPath",
        "sessionIdentity",
        "hostBinding",
        "queryHash",
        "orderingVersion",
        "snapshotHash",
        "offset",
    };
    private static readonly string[] SessionIdentityMembers =
    {
        "workerSessionId",
        "sessionGeneration",
        "portalProcessId",
        "projectPath",
    };
    private static readonly string[] HostBindingMembers =
    {
        "isBound",
        "bindingId",
        "revision",
        "normalizedProjectPath",
    };

    private readonly byte[] _key;

    internal HardwarePageCursorCodec(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException("Hardware page cursor keys must contain exactly 32 bytes.", nameof(key));
        }

        _key = (byte[])key.Clone();
    }

    internal static HardwarePageCursorCodec CreateProcessScoped()
    {
        var key = new byte[KeySizeBytes];
        RandomNumberGenerator.Fill(key);
        return new HardwarePageCursorCodec(key);
    }

    internal string Encode(HardwarePageCursorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateState(state);

        var payloadBytes = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(state));
        var signature = ComputeSignature(payloadBytes);
        return $"{EncodeBase64Url(payloadBytes)}.{EncodeBase64Url(signature)}";
    }

    internal HardwarePageCursorState Decode(string cursor)
    {
        try
        {
            var parts = cursor?.Split('.') ?? Array.Empty<string>();
            if (parts.Length != 2)
            {
                throw new FormatException();
            }

            var payloadBytes = DecodeBase64Url(parts[0]);
            var suppliedSignature = DecodeBase64Url(parts[1]);
            var expectedSignature = ComputeSignature(payloadBytes);
            if (suppliedSignature.Length != SignatureSizeBytes
                || !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            {
                throw new CryptographicException();
            }

            var json = StrictUtf8.GetString(payloadBytes);
            ValidateExactPayload(json);
            var state = CanonicalJson.Deserialize<HardwarePageCursorState>(json);
            ValidateState(state);
            var canonicalPayloadBytes = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(state));
            if (!payloadBytes.AsSpan().SequenceEqual(canonicalPayloadBytes))
            {
                throw new JsonException("Cursor payload must use canonical JSON.");
            }

            return state;
        }
        catch (Exception exception) when (exception is ArgumentException
            or CryptographicException
            or DecoderFallbackException
            or FormatException
            or JsonException)
        {
            throw new HardwarePageCursorException();
        }
    }

    private byte[] ComputeSignature(byte[] payloadBytes)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(payloadBytes);
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

    private static void ValidateExactPayload(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var root = document.RootElement;
        ValidateExactObject(root, PayloadMembers);
        ValidateExactObject(root.GetProperty("sessionIdentity"), SessionIdentityMembers);
        ValidateExactObject(root.GetProperty("hostBinding"), HostBindingMembers);
    }

    private static void ValidateExactObject(JsonElement element, IReadOnlyCollection<string> requiredMembers)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Cursor payload member must be an object.");
        }

        var required = new HashSet<string>(requiredMembers, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!required.Contains(property.Name) || !seen.Add(property.Name))
            {
                throw new JsonException("Cursor payload members are invalid.");
            }
        }

        if (seen.Count != required.Count)
        {
            throw new JsonException("Cursor payload members are incomplete.");
        }
    }

    private static void ValidateState(HardwarePageCursorState state)
    {
        var normalizedResolvedPath = ProjectPathNormalization.Canonicalize(state.ResolvedProjectPath);
        if (state.Version != CurrentVersion
            || normalizedResolvedPath is null
            || !string.Equals(state.ResolvedProjectPath, normalizedResolvedPath, StringComparison.OrdinalIgnoreCase)
            || state.SessionIdentity is null
            || string.IsNullOrWhiteSpace(state.SessionIdentity.WorkerSessionId)
            || state.SessionIdentity.SessionGeneration < 0
            || state.SessionIdentity.PortalProcessId is null
            || state.SessionIdentity.PortalProcessId <= 0
            || !SameProject(state.SessionIdentity.ProjectPath, state.ResolvedProjectPath)
            || state.HostBinding is null
            || !IsValidHostBinding(state.HostBinding)
            || (state.HostBinding.IsBound
                && !SameProject(state.HostBinding.NormalizedProjectPath, state.ResolvedProjectPath))
            || !LowercaseSha256.IsMatch(state.QueryHash ?? string.Empty)
            || state.OrderingVersion <= 0
            || !LowercaseSha256.IsMatch(state.SnapshotHash ?? string.Empty)
            || state.Offset < 0)
        {
            throw new JsonException("Cursor payload values are invalid.");
        }
    }

    private static bool IsValidHostBinding(ProjectBindingCursorState binding)
    {
        if (!binding.IsBound)
        {
            return binding.BindingId is null
                && binding.Revision is null
                && binding.NormalizedProjectPath is null;
        }

        var normalizedPath = ProjectPathNormalization.Canonicalize(binding.NormalizedProjectPath);
        return !string.IsNullOrWhiteSpace(binding.BindingId)
            && binding.Revision is >= 0
            && normalizedPath is not null
            && string.Equals(binding.NormalizedProjectPath, normalizedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameProject(string? first, string? second)
    {
        var normalizedFirst = ProjectPathNormalization.Canonicalize(first);
        var normalizedSecond = ProjectPathNormalization.Canonicalize(second);
        return normalizedFirst is not null
            && normalizedSecond is not null
            && string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class HardwarePageCursorValidator
{
    internal static string? Validate(
        HardwarePageCursorState state,
        NetworkOperationRequest request,
        ProjectBindingSnapshot currentBinding)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(currentBinding);

        var incomingQueryHash = HardwarePageEvidence.CreateQueryHash(
            request.DeviceName,
            request.PlcName,
            request.IncludeIoDetails,
            request.IncludeTagMatches);
        if (!string.Equals(state.QueryHash, incomingQueryHash, StringComparison.Ordinal))
        {
            return WorkerFailureCategories.CursorFilterMismatch;
        }

        if (request.ProjectPath is not null
            && !SameProject(request.ProjectPath, state.ResolvedProjectPath))
        {
            return WorkerFailureCategories.CursorBindingMismatch;
        }

        return state.HostBinding.Matches(currentBinding)
            ? null
            : WorkerFailureCategories.CursorBindingMismatch;
    }

    private static bool SameProject(string? first, string? second)
    {
        var normalizedFirst = ProjectPathNormalization.Canonicalize(first);
        var normalizedSecond = ProjectPathNormalization.Canonicalize(second);
        return normalizedFirst is not null
            && normalizedSecond is not null
            && string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
    }
}
