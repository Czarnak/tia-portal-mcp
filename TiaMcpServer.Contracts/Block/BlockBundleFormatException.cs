namespace TiaMcpServer.Contracts.Block;

/// <summary>
/// A block bundle that does not follow <see cref="BlockBundleText"/>'s format. Always a caller
/// error: the worker adapters translate it into a <c>validation_error</c> with the same message.
/// </summary>
public sealed class BlockBundleFormatException : Exception
{
    public BlockBundleFormatException(string message)
        : base(message)
    {
    }
}
