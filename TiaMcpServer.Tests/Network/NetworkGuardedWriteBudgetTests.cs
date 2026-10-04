using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkGuardedWriteBudgetTests
{
    [Fact]
    public void AdmittedEscapedOperationIds_MinimalProtectedEnvelopeFitsDocumentBudget()
    {
        var operations = Enumerable.Range(0, NetworkOperationCatalog.MaxBatchSize)
            .Select(index => new NetworkOperationRequest
            {
                OperationId = new string('\u4e00', NetworkOperationCatalog.MaxOperationIdLength - 1)
                    + (char)('\u4e01' + index),
                Operation = "delete_subnet",
                Target = new NetworkObjectTarget { Kind = "subnet", SubnetId = "subnet-1" },
            }).ToArray();
        var validation = NetworkOperationCatalog.ValidateWrite(operations);
        Assert.True(validation.IsValid, validation.Error);
        Assert.Equal(50, operations.Select(operation => operation.OperationId).Distinct().Count());
        Assert.All(operations, operation => Assert.Equal(256, operation.OperationId.Length));

        // A strict lower bound: no effect/result/evidence, omission metadata, diagnostics,
        // guards, warnings, truncation metadata, or final checks. Only required roots,
        // exact identities, and trusted execution/verification summaries remain.
        var response = new NetworkGuardedWriteResponse("network_write", "1.0", "applied", false,
            null, Array.Empty<string>(), Array.Empty<WriteGuardReport>(),
            operations.Select(operation => new NetworkWriteEffectPresentation(operation.OperationId, null, null)).ToArray(),
            StructuredOperationBatch.FromItems(operations.Select(operation => new StructuredOperationItem(
                operation.OperationId, operation.Operation, "succeeded", null, null, null, null,
                Array.Empty<string>())).ToArray()),
            new NetworkWriteVerification(true, operations.Select(operation => new NetworkOperationVerification(
                operation.OperationId, operation.Operation, "passed", null, null)).ToArray(),
                Array.Empty<NetworkVerificationCheckInfo>(), null));
        var canonical = CanonicalJson.Serialize(response);

        Assert.True(canonical.Length <= 180000,
            $"Admitted minimal protected envelope is {canonical.Length} canonical characters; cap is 180000. "
            + "All 50 unique IDs have 256 UTF-16 characters and occur in three required identity arrays.");
    }
}
