using System.Globalization;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

internal sealed class NetworkObjectDiscoveryEvidenceValue<T>
{
    private NetworkObjectDiscoveryEvidenceValue(
        bool isUsable,
        T value,
        string diagnostic,
        string snapshotToken)
    {
        IsUsable = isUsable;
        Value = value;
        Diagnostic = diagnostic;
        SnapshotToken = snapshotToken;
    }

    public bool IsUsable { get; }
    public T Value { get; }
    public string Diagnostic { get; }
    public string SnapshotToken { get; }

    public static NetworkObjectDiscoveryEvidenceValue<T> Usable(T value, string snapshotToken)
        => new(true, value, string.Empty, snapshotToken);

    public static NetworkObjectDiscoveryEvidenceValue<T> Unusable(string diagnostic, string snapshotToken)
        => new(false, default!, diagnostic, snapshotToken);
}

internal static class NetworkObjectDiscoveryEvidence
{
    public static NetworkObjectDiscoveryEvidenceValue<string> ReadString(object? value, string field)
    {
        if (value is null)
        {
            return NetworkObjectDiscoveryEvidenceValue<string>.Unusable(
                $"{field} was null; selector not available.",
                "null");
        }

        if (value is not string text)
        {
            return NetworkObjectDiscoveryEvidenceValue<string>.Unusable(
                $"{field} had an unexpected CLR type; selector not available.",
                "wrongType");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return NetworkObjectDiscoveryEvidenceValue<string>.Unusable(
                $"{field} was blank; selector not available.",
                "blank");
        }

        return NetworkObjectDiscoveryEvidenceValue<string>.Usable(text, "value:" + text);
    }

    public static NetworkObjectDiscoveryEvidenceValue<int> ReadInt(object? value, string field)
    {
        if (value is null)
        {
            return NetworkObjectDiscoveryEvidenceValue<int>.Unusable(
                $"{field} was null; selector not available.",
                "null");
        }

        if (value is not int number)
        {
            return NetworkObjectDiscoveryEvidenceValue<int>.Unusable(
                $"{field} had an unexpected CLR type; selector not available.",
                "wrongType");
        }

        if (number < 0)
        {
            return NetworkObjectDiscoveryEvidenceValue<int>.Unusable(
                $"{field} was negative; selector not available.",
                "negative");
        }

        return NetworkObjectDiscoveryEvidenceValue<int>.Usable(
            number,
            "value:" + number.ToString(CultureInfo.InvariantCulture));
    }

    public static NetworkObjectDiscoveryEvidenceValue<string> UnreadableString(string field)
        => NetworkObjectDiscoveryEvidenceValue<string>.Unusable(
            $"{field} could not be read; selector not available.",
            "readFailed");

    public static NetworkObjectDiscoveryEvidenceValue<int> UnreadableInt(string field)
        => NetworkObjectDiscoveryEvidenceValue<int>.Unusable(
            $"{field} could not be read; selector not available.",
            "readFailed");

    /// <summary>
    /// Adds an unusable value's diagnostic, which already names the field, to read messages.
    /// With <paramref name="reportNull"/> false, a successfully read null is ordinary data and adds
    /// nothing; blank, wrong-type and unreadable values still report.
    /// </summary>
    public static void AddReadMessage<T>(
        List<string> messages,
        NetworkObjectDiscoveryEvidenceValue<T> evidence,
        bool reportNull = true)
    {
        if (!evidence.IsUsable && (reportNull || evidence.SnapshotToken != "null"))
        {
            messages.Add(evidence.Diagnostic);
        }
    }
}
