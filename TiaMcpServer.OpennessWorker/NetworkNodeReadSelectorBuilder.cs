using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker;

internal static class NetworkNodeReadSelectorBuilder
{
    public static void Apply(DeviceInfo device, bool traversalComplete) { }
    public static NetworkInterfacePathMatch<TNode> MatchNode<TNode>(IEnumerable<TNode> nodes,
        string? nodeId, int? nodeIndex, Func<TNode, string?> identity) where TNode : class
        => NetworkInterfacePathMatch<TNode>.Fail(WorkerFailureCategories.TargetNotFound, "Not implemented.");
}
