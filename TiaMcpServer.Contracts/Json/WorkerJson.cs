using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts.Json;

/// <summary>
/// The single definition of the host-worker wire format.
///
/// <para>
/// <see cref="Envelope"/> reads <see cref="WorkerRequest"/> and <see cref="WorkerResponse"/> and
/// writes responses; <see cref="Request"/> writes requests. <see cref="SerializePayload{T}"/>
/// renders the <see cref="WorkerResponse.Payload"/> document: members are written even when null,
/// except members explicitly declared conditional.
/// </para>
/// </summary>
public static class WorkerJson
{
    /// <summary>Envelope reads (both directions) and response writes: camelCase, case-insensitive read, null members omitted.</summary>
    public static JsonSerializerOptions Envelope { get; } = Create(JsonIgnoreCondition.WhenWritingNull);

    /// <summary>Request writes: every member is written, null or not, so a request states what it leaves unset.</summary>
    public static JsonSerializerOptions Request { get; } = Create(JsonIgnoreCondition.Never);

    /// <summary>Payload options: every member is written, null or not, unless explicitly conditional.</summary>
    public static JsonSerializerOptions ExplicitNullPayload { get; } = Create(JsonIgnoreCondition.Never);

    public static string SerializePayload<T>(T payload)
        => JsonSerializer.Serialize(payload, ExplicitNullPayload);

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
