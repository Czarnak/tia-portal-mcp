using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

/// <summary>
/// The single definition of the host-worker wire format.
///
/// <para>
/// <see cref="Envelope"/> reads <see cref="WorkerRequest"/> and <see cref="WorkerResponse"/> and
/// writes responses; <see cref="Request"/> writes requests. <see cref="SerializePayload{T}"/>
/// renders the <see cref="WorkerResponse.Payload"/> document: members are written even when null,
/// unless the payload contract carries <see cref="LegacyNullOmissionAttribute"/>. A collection
/// payload follows its element contract.
/// </para>
/// </summary>
public static class WorkerJson
{
    /// <summary>Envelope reads (both directions) and response writes: camelCase, case-insensitive read, null members omitted.</summary>
    public static JsonSerializerOptions Envelope { get; } = Create(JsonIgnoreCondition.WhenWritingNull);

    /// <summary>Request writes: every member is written, null or not, so a request states what it leaves unset.</summary>
    public static JsonSerializerOptions Request { get; } = Create(JsonIgnoreCondition.Never);

    /// <summary>Payload options for unmarked contracts: every member is written, null or not.</summary>
    public static JsonSerializerOptions ExplicitNullPayload { get; } = Create(JsonIgnoreCondition.Never);

    /// <summary>Payload options for contracts marked with <see cref="LegacyNullOmissionAttribute"/>.</summary>
    public static JsonSerializerOptions LegacyNullOmittingPayload { get; } = Create(JsonIgnoreCondition.WhenWritingNull);

    public static string SerializePayload<T>(T payload)
        => JsonSerializer.Serialize(payload, PayloadOptionsFor(payload?.GetType() ?? typeof(T)));

    public static JsonSerializerOptions PayloadOptionsFor(Type payloadType)
        => OmitsNullMembers(payloadType) ? LegacyNullOmittingPayload : ExplicitNullPayload;

    public static bool OmitsNullMembers(Type payloadType)
    {
        if (payloadType is null)
        {
            throw new ArgumentNullException(nameof(payloadType));
        }

        return ContractType(payloadType)
            .GetCustomAttribute<LegacyNullOmissionAttribute>(inherit: false) is not null;
    }

    private static Type ContractType(Type type)
    {
        if (type == typeof(string))
        {
            return type;
        }

        if (type.IsArray)
        {
            return type.GetElementType()!;
        }

        var enumerable = new[] { type }
            .Concat(type.GetInterfaces())
            .FirstOrDefault(candidate => candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0] ?? type;
    }

    private static JsonSerializerOptions Create(JsonIgnoreCondition nullMembers)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = nullMembers
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
