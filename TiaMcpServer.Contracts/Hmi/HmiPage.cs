namespace TiaMcpServer.Contracts.Hmi;

/// <summary>Stateless page position of a paged operation; <see cref="NextOffset"/> is null on the last page.</summary>
public sealed record HmiPage(int Offset, int Limit, int Total, int? NextOffset);
