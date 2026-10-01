using TiaMcpServer.Contracts;

namespace TiaMcpServer.Safety.Pipeline;

public enum ConfirmationMode { AskUser, Policy }

/// <summary>Full mode satisfies acknowledge guards by policy; all other modes ask the client.</summary>
public static class WriteConfirmationPolicy
{
    public static ConfirmationMode For(McpAccessMode mode)
        => mode == McpAccessMode.Full ? ConfirmationMode.Policy : ConfirmationMode.AskUser;
}
