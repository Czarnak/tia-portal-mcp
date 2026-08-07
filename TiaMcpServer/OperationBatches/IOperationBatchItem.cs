namespace TiaMcpServer.OperationBatches;

public interface IOperationBatchItem
{
    string OperationId { get; }
    string Operation { get; }
    string? ProjectPath { get; }
}
