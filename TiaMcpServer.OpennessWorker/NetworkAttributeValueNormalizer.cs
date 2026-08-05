using System;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker;

public sealed class NetworkAttributeNormalizationResult
{
    public bool IsRepresentable { get; set; }
    public NetworkAttributeValueInfo? Value { get; set; }
    public string? ClrTypeName { get; set; }
}

public static class NetworkAttributeValueNormalizer
{
    public static NetworkAttributeNormalizationResult Normalize(object? input)
    {
        if (input is null)
        {
            return new NetworkAttributeNormalizationResult
            {
                IsRepresentable = true,
                Value = new NetworkAttributeValueInfo
                {
                    Kind = "null",
                    Value = null,
                    TypeName = null,
                },
            };
        }

        var typeName = input.GetType().FullName;
        if (input is string text)
        {
            return Representable("string", text, typeName);
        }

        if (input is char character)
        {
            return Representable("string", character.ToString(), typeName);
        }

        if (input is bool boolean)
        {
            return Representable("boolean", boolean, typeName);
        }

        if (input is Enum enumValue)
        {
            return NormalizeEnum(enumValue, typeName);
        }

        if (input is sbyte || input is byte || input is short || input is ushort || input is int || input is uint || input is long)
        {
            return Representable("integer", Convert.ToInt64(input), typeName);
        }

        if (input is ulong unsignedLong)
        {
            return unsignedLong <= long.MaxValue
                ? Representable("integer", (long)unsignedLong, typeName)
                : Unrepresentable(typeName);
        }

        if (input is float single)
        {
            return float.IsNaN(single) || float.IsInfinity(single)
                ? Unrepresentable(typeName)
                : Representable("number", single, typeName);
        }

        if (input is double dbl)
        {
            return double.IsNaN(dbl) || double.IsInfinity(dbl)
                ? Unrepresentable(typeName)
                : Representable("number", dbl, typeName);
        }

        if (input is decimal decimalValue)
        {
            return Representable("number", decimalValue, typeName);
        }

        return Unrepresentable(typeName);
    }

    private static NetworkAttributeNormalizationResult NormalizeEnum(Enum value, string? typeName)
    {
        try
        {
            var numericValue = Convert.ToInt64(value);
            return Representable(
                "enum",
                new NetworkEnumValueInfo
                {
                    TypeName = typeName ?? string.Empty,
                    Symbol = Enum.GetName(value.GetType(), value) ?? string.Empty,
                    NumericValue = numericValue,
                },
                typeName);
        }
        catch (OverflowException)
        {
            return Unrepresentable(typeName);
        }
    }

    private static NetworkAttributeNormalizationResult Representable(string kind, object value, string? typeName)
        => new()
        {
            IsRepresentable = true,
            Value = new NetworkAttributeValueInfo
            {
                Kind = kind,
                Value = value,
                TypeName = typeName,
            },
        };

    private static NetworkAttributeNormalizationResult Unrepresentable(string? typeName)
        => new() { ClrTypeName = typeName };
}
