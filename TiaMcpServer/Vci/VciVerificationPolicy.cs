using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Vci;

/// <summary>One key per workspace (case-insensitive): a later item on the same workspace supersedes an earlier one.</summary>
internal sealed class VciVerificationPolicy : IWriteVerificationPolicy<VciOperationRequest>
{
    public string ReadTool => "vci_read";
    public bool HasReadBack(VciOperationRequest item) => false;
    public Task<WriteOperationVerification> ReadBackAsync(VciOperationRequest item, StructuredOperationFailure? failure)
        => throw new NotSupportedException();
    public IReadOnlyList<string> ObjectKeys(VciOperationRequest item, bool asLater)
        => ["vci\nworkspace\n" + item.WorkspaceName!.ToUpperInvariant()];
}
