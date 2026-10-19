using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

internal static class NetworkOpennessRead
{
    // Property failures may degrade a read; Portal loss and programming errors must propagate.
    internal static bool IsRecoverable(Exception exception)
        => exception is EngineeringException && exception is not NonRecoverableException;

    private static bool TryRead<T>(Func<T> read, out T value, out string? error)
    {
        try
        {
            value = read();
            error = null;
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            value = default!;
            error = exception.Message;
            return false;
        }
    }

    public static NetworkObjectDiscoveryEvidenceValue<string> ReadTypedIdentityString(Func<string?> read, string field)
        => TryRead(read, out var value, out _)
            ? NetworkObjectDiscoveryEvidence.ReadString(value, field)
            : NetworkObjectDiscoveryEvidence.UnreadableString(field);

    public static NetworkObjectDiscoveryEvidenceValue<int> ReadTypedIdentityInt(Func<int> read, string field)
        => TryRead(read, out var value, out _)
            ? NetworkObjectDiscoveryEvidence.ReadInt(value, field)
            : NetworkObjectDiscoveryEvidence.UnreadableInt(field);

    public static NetworkObjectDiscoveryEvidenceValue<string> ReadExactStringIdentityAttribute(
        IEngineeringObject engineeringObject, string attributeName, string field)
        => TryRead(() => engineeringObject.GetAttribute(attributeName), out var value, out _)
            ? NetworkObjectDiscoveryEvidence.ReadString(value, field)
            : NetworkObjectDiscoveryEvidence.UnreadableString(field);

    public static string? ReadOptionalString(Func<string?> read, string description, List<string>? messages)
    {
        var success = TryRead(read, out var value, out var error);
        AddReadMessage(messages, description, error);
        return success ? value : null;
    }

    public static bool? ReadOptionalBool(Func<bool> read, string description, List<string>? messages)
    {
        var success = TryRead(read, out var value, out var error);
        AddReadMessage(messages, description, error);
        return success ? value : null;
    }

    public static int? ReadOptionalInt(Func<int> read, string description, List<string>? messages)
    {
        var success = TryRead(read, out var value, out var error);
        AddReadMessage(messages, description, error);
        return success ? value : null;
    }

    public static int? ReadOptionalNonNegativeInt(Func<int> read, string description, List<string>? messages)
    {
        var value = ReadOptionalInt(read, description, messages);
        if (value < 0)
        {
            AddReadMessage(messages, description, "the reported value was negative.");
            return null;
        }

        return value;
    }

    public static string? ReadOptionalEnumName<TEnum>(Func<TEnum> read, string description, List<string>? messages)
        where TEnum : struct, Enum
    {
        var success = TryReadEnumName(read, out var value, out var error);
        AddReadMessage(messages, description, error);
        return success ? value : null;
    }

    public static bool TryReadEnumName<TEnum>(Func<TEnum> read, out string? value, out string? error)
        where TEnum : struct, Enum
        => TryRead(() => Enum.Format(typeof(TEnum), read(), "G"), out value, out error);

    public static string? ReadExactStringAttribute(
        IEngineeringObject engineeringObject, string attributeName, string description, List<string>? messages)
    {
        var success = TryReadStringAttribute(engineeringObject, attributeName, out var value, out var error);
        AddReadMessage(messages, description, error);
        return success ? value : null;
    }

    public static bool TryReadStringAttribute(
        IEngineeringObject engineeringObject, string attributeName, out string? value, out string? error)
    {
        if (!TryRead(() => engineeringObject.GetAttribute(attributeName), out var raw, out error))
        {
            value = null;
            return false;
        }

        value = raw as string;
        error = raw is null || raw is string
            ? null
            : $"returned unsupported CLR type '{raw.GetType().FullName}'.";
        return error is null;
    }

    public static void AddReadMessage(List<string>? messages, string description, string? error)
    {
        if (error is not null)
        {
            messages?.Add($"Could not read {description}: {error}");
        }
    }

    public static List<string> CombineDiagnostics(IEnumerable<string> inherited, params string[] diagnostics)
        => inherited.Concat(diagnostics)
            .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<T> FilterByDeviceName<T>(
        IReadOnlyList<T> candidates,
        string? deviceName,
        Func<T, NetworkObjectDiscoveryEvidenceValue<string>> nameEvidence,
        out string? failureMessage)
    {
        failureMessage = null;
        if (deviceName is null)
        {
            return candidates;
        }

        var matches = candidates.Where(candidate =>
        {
            var name = nameEvidence(candidate);
            return name.IsUsable && string.Equals(name.Value, deviceName, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        if (matches.Count == 1)
        {
            return matches;
        }

        failureMessage = matches.Count == 0
            ? $"No device named '{deviceName}' was found; no devices are reported."
            : $"More than one device matches '{deviceName}'; no devices are reported because the device filter is ambiguous.";
        return Array.Empty<T>();
    }
}
