using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker;

public sealed class NetworkAttributeObservation
{
    public string Name { get; set; } = string.Empty;
    public Func<object?>? ReadValue { get; set; }
    public bool? CanRead { get; set; }
    public bool? CanWrite { get; set; }
    public IReadOnlyList<string> SupportedTypes { get; set; } = Array.Empty<string>();
    public string Availability { get; set; } = "available";
}

public static class NetworkAttributeResultBuilder
{
    public static IReadOnlyList<NetworkAttributeInfo> Build(
        IEnumerable<NetworkAttributeObservation> modeled,
        IEnumerable<NetworkAttributeObservation> dynamic,
        IReadOnlyList<string>? attributeNames = null)
    {
        var modeledByName = GroupByName(modeled);
        var dynamicByName = GroupByName(dynamic);
        var names = attributeNames ?? modeledByName.Keys
            .Concat(dynamicByName.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return names.Select(name => BuildAttribute(
            name,
            modeledByName.TryGetValue(name, out var modeledEntries) ? modeledEntries : Array.Empty<NetworkAttributeObservation>(),
            dynamicByName.TryGetValue(name, out var dynamicEntries) ? dynamicEntries : Array.Empty<NetworkAttributeObservation>()))
            .ToArray();
    }

    private static Dictionary<string, List<NetworkAttributeObservation>> GroupByName(IEnumerable<NetworkAttributeObservation> observations)
        => observations.GroupBy(observation => observation.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

    private static NetworkAttributeInfo BuildAttribute(
        string name,
        IReadOnlyList<NetworkAttributeObservation> modeled,
        IReadOnlyList<NetworkAttributeObservation> dynamic)
    {
        if (modeled.Count == 0 && dynamic.Count == 0)
        {
            return new NetworkAttributeInfo
            {
                Name = name,
                Access = "unknown",
                Availability = "unknownAttribute",
            };
        }

        var all = modeled.Concat(dynamic).ToArray();
        var result = new NetworkAttributeInfo
        {
            Name = name,
            Source = modeled.Count > 0 && dynamic.Count > 0 ? "modeledAndDynamic" : modeled.Count > 0 ? "modeled" : "dynamic",
            Access = GetAccess(all),
            SupportedTypes = all.SelectMany(observation => observation.SupportedTypes)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(type => type, StringComparer.Ordinal)
                .ToList(),
        };

        var modeledRead = ReadFirst(modeled);
        var dynamicRead = ReadFirst(dynamic);
        var preferred = modeledRead.Success ? modeledRead : dynamicRead;

        if (preferred.Success)
        {
            result.Availability = preferred.Normalization!.IsRepresentable ? "available" : "unrepresentable";
            result.Value = preferred.Normalization.Value;
            if (!preferred.Normalization.IsRepresentable)
            {
                result.Diagnostic = Diagnostic("unrepresentable", "The attribute value cannot be represented by the public contract.", preferred.Normalization.ClrTypeName);
            }
            else if (modeledRead.Success && dynamicRead.Success && !ValuesEqual(modeledRead.Normalization!.Value, dynamicRead.Normalization!.Value))
            {
                result.Diagnostic = Diagnostic("source_disagreement", "Modeled and dynamic reads returned different values.", null);
            }

            return result;
        }

        var failure = modeledRead.Failure ?? dynamicRead.Failure;
        if (failure is not null)
        {
            result.Availability = "readFailed";
            result.Diagnostic = Diagnostic("read_error", failure.Message, null);
            return result;
        }

        result.Availability = all.Select(observation => observation.Availability)
            .FirstOrDefault(availability => availability != "available") ?? "unreadable";
        return result;
    }

    private static AttributeReadResult ReadFirst(IEnumerable<NetworkAttributeObservation> observations)
    {
        Exception? failure = null;
        foreach (var observation in observations)
        {
            if (observation.Availability != "available" || observation.ReadValue is null)
            {
                continue;
            }

            try
            {
                return AttributeReadResult.FromNormalization(NetworkAttributeValueNormalizer.Normalize(observation.ReadValue()));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        return AttributeReadResult.FromFailure(failure);
    }

    private static string GetAccess(IEnumerable<NetworkAttributeObservation> observations)
    {
        var entries = observations.ToArray();
        var canRead = CombineCapability(entries.Select(observation => observation.CanRead));
        var canWrite = CombineCapability(entries.Select(observation => observation.CanWrite));
        if (!canRead.HasValue || !canWrite.HasValue)
        {
            return "unknown";
        }

        return canRead.Value
            ? canWrite.Value ? "readWrite" : "readOnly"
            : canWrite.Value ? "writeOnly" : "none";
    }

    private static bool? CombineCapability(IEnumerable<bool?> capabilities)
    {
        var values = capabilities.ToArray();
        return values.Any(value => value == true) ? true
            : values.All(value => value == false) ? false
            : null;
    }

    private static bool ValuesEqual(NetworkAttributeValueInfo? left, NetworkAttributeValueInfo? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (!string.Equals(left.Kind, right.Kind, StringComparison.Ordinal))
        {
            return false;
        }

        if (left.Value is NetworkEnumValueInfo leftEnum && right.Value is NetworkEnumValueInfo rightEnum)
        {
            return leftEnum.TypeName == rightEnum.TypeName
                && leftEnum.Symbol == rightEnum.Symbol
                && leftEnum.NumericValue == rightEnum.NumericValue;
        }

        return Equals(left.Value, right.Value);
    }

    private static NetworkAttributeDiagnosticInfo Diagnostic(string category, string message, string? clrTypeName)
        => new() { Category = category, Message = message, ClrTypeName = clrTypeName };

    private sealed class AttributeReadResult
    {
        public bool Success { get; private set; }
        public NetworkAttributeNormalizationResult? Normalization { get; private set; }
        public Exception? Failure { get; private set; }

        public static AttributeReadResult FromNormalization(NetworkAttributeNormalizationResult normalization)
            => new() { Success = true, Normalization = normalization };

        public static AttributeReadResult FromFailure(Exception? failure)
            => new() { Failure = failure };
    }
}
