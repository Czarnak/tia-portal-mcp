namespace TiaMcpServer.OpennessWorker.Openness.Block;

internal static class BlockCreationCoordinator
{
    public static TResult Execute<TResult>(
        Func<TResult> importBlock,
        Func<BlockPostconditionEvidence> verifyPostcondition)
    {
        if (importBlock is null) throw new ArgumentNullException(nameof(importBlock));
        if (verifyPostcondition is null) throw new ArgumentNullException(nameof(verifyPostcondition));

        var result = importBlock();
        BlockPostconditionVerifier.Verify(verifyPostcondition(), "create");
        return result;
    }
}
