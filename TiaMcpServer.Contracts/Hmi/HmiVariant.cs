using System.Text.Json;

namespace TiaMcpServer.Contracts.Hmi;

/// <summary>
/// A <c>System.Object</c> property value. <see cref="Type"/> is the runtime type's FullName (null when
/// the value is null); <see cref="Value"/> is a real JSON scalar (string, number or boolean), never an
/// escaped JSON string.
/// </summary>
public sealed record HmiVariant(string? Type, JsonElement? Value);
