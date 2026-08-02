using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Network;

/// <summary>A tool-level failure that prevented the batch from running at all.</summary>
public sealed record NetworkToolError(string Category, string Message);

/// <summary>
/// Declared output schema of <c>network_read</c>.
///
/// <para>
/// Exactly one of <see cref="Batch"/> and <see cref="Error"/> is populated: <see cref="Error"/>
/// when validation or access control rejected the call before any worker ran, otherwise
/// <see cref="Batch"/>. <see cref="Success"/> describes the whole call — a batch that ran but
/// contains failed items reports <c>false</c> here while remaining a successful MCP result.
/// </para>
/// </summary>
public sealed record NetworkReadResponse(
    string Tool,
    bool Success,
    StructuredOperationBatch? Batch,
    NetworkToolError? Error);
